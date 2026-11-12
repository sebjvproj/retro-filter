namespace RetroFilter;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Mode test sans interface : RetroFilter --snapshot "<partie du titre>" <dossier> [options]
        // Options : taille de pixel (nombre, 0 = 320x200), « rapide » (moyenne 2x2), « palette=N » (index dans FilterModes.AllPalettes).
        if (args.Length >= 3 && args[0] == "--snapshot")
        {
            var options = args.Skip(3).ToList();
            int pixelSize = options.Where(o => o.All(char.IsDigit)).Select(int.Parse).DefaultIfEmpty(1).First();
            int palette = options.Where(o => o.StartsWith("palette=")).Select(o => int.Parse(o[8..])).DefaultIfEmpty(0).First();
            return Snapshot(args[1], args[2], pixelSize, options.Contains("rapide"), palette);
        }

        // Mesure du débit de capture avec filtre : RetroFilter --fps "<partie du titre>" <priorité 0 normale, 1 haute, 2 temps réel>
        // Options à la fin : « brut » mesure la capture seule, sans filtre ; « plafond » garde la limite de Windows.
        if (args.Length >= 3 && args[0] == "--fps")
            return Fps(args[1], int.Parse(args[2]), args.Skip(3).Contains("brut"), args.Skip(3).Contains("plafond"));

        // Test de l'overlay : le pose 3 s sur la fenêtre, puis enregistre ce qui est à l'écran à cet endroit.
        if (args.Length == 3 && args[0] == "--overlay")
            return OverlayTest(args[1], args[2]);

        // Compare dither.hlsl à la v2 de référence (cga-v2.hlsl) : RetroFilter --verify "<partie du titre>"
        if (args.Length == 2 && args[0] == "--verify")
            return Verify(args[1]);

        // Test de l'interface : RetroFilter --ui-test <dossier> (menu de la zone de notification, icône)
        if (args.Length == 2 && args[0] == "--ui-test")
            return UiTest(args[1]);

        // Mesure du temps GPU de la passe CGA : RetroFilter --bench "<partie du titre>"
        if (args.Length == 2 && args[0] == "--bench")
            return Bench(args[1]);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>
    /// Capture une image de la fenêtre et enregistre dans le dossier : source.png (brute),
    /// cga-small.png (sortie de la passe CGA) et cga.png (agrandie à la taille de la capture).
    /// </summary>
    static int Snapshot(string titlePart, string directory, int pixelSize, bool fastSampling, int palette)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        Directory.CreateDirectory(directory);
        using var device = GpuDevice.Create();
        using var filter = new CgaFilter(device);
        filter.FastSampling = fastSampling;
        FilterModes.ApplyPalette(filter.Settings, FilterModes.AllPalettes[palette].Palette);
        filter.Settings.PixelSize = pixelSize;
        using var capture = new WindowCapture(device, window.Handle);
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        capture.FrameArrived += (texture, width, height) =>
        {
            if (done.IsSet) return;
            try
            {
                filter.SetSource(texture, width, height);
                filter.Apply();
                filter.SaveSource(Path.Combine(directory, "source.png"));
                filter.SaveSmall(Path.Combine(directory, "cga-small.png"));
                filter.SaveOutput(Path.Combine(directory, "cga.png"));
                Console.WriteLine($"{window.Title} : {width}x{height} -> {directory}");
                Console.WriteLine(WindowList.DescribeGeometry(window.Handle));
            }
            catch (Exception ex)
            {
                error = ex;
            }
            done.Set();
        };

        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            Console.Error.WriteLine("Aucune image reçue en 5 secondes.");
            return 3;
        }
        if (error != null)
        {
            Console.Error.WriteLine(error);
            return 4;
        }
        return 0;
    }

    /// <summary>Capture une image puis mesure le temps moyen de la passe CGA pour chaque taille de pixel.</summary>
    static int Bench(string titlePart)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        using var device = GpuDevice.Create();
        using var filter = new CgaFilter(device);
        using var done = new ManualResetEventSlim();
        using (var capture = new WindowCapture(device, window.Handle))
        {
            capture.FrameArrived += (texture, width, height) =>
            {
                if (done.IsSet) return;
                filter.SetSource(texture, width, height);
                done.Set();
            };
            if (!done.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.Error.WriteLine("Aucune image reçue en 5 secondes.");
                return 3;
            }
        }

        using (var dxgiDevice = device.QueryInterface<Vortice.DXGI.IDXGIDevice>())
        using (var adapter = dxgiDevice.GetAdapter())
            Console.WriteLine($"GPU : {adapter.Description.Description}");
        Console.WriteLine($"Capture {filter.SourceWidth}x{filter.SourceHeight}");

        foreach (int pixelSize in new[] { 1, 2, 3, 4, 0 })
        {
            filter.Settings.PixelSize = pixelSize;
            string name = pixelSize == 0 ? "historique 320x200" : $"pixel {pixelSize}";
            Console.WriteLine($"{name,-24} {Measure(filter),7:F2} ms par image");
        }

        filter.Settings.PixelSize = 1;
        filter.FastSampling = true;
        Console.WriteLine($"{"pixel 1, 2x2 lectures",-24} {Measure(filter),7:F2} ms par image");
        filter.FastSampling = false;

        // Coût selon la palette, en mode moderne (pixel 1).
        foreach (var (machine, palette) in FilterModes.AllPalettes)
        {
            FilterModes.ApplyPalette(filter.Settings, palette);
            Console.WriteLine($"pixel 1, {machine.Name + " " + palette.Name,-34} {Measure(filter),7:F2} ms par image");
        }
        return 0;
    }

    /// <summary>
    /// Sur une même image capturée, pour chaque palette et en modes pixel 1 et 320x200 :
    /// pixels différents entre dither.hlsl et la v2 de référence, et temps de chacun.
    /// </summary>
    static int Verify(string titlePart)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        using var device = GpuDevice.Create();
        using var filter = new CgaFilter(device);
        using var done = new ManualResetEventSlim();
        using (var capture = new WindowCapture(device, window.Handle))
        {
            capture.FrameArrived += (texture, width, height) =>
            {
                if (done.IsSet) return;
                filter.SetSource(texture, width, height);
                done.Set();
            };
            if (!done.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.Error.WriteLine("Aucune image reçue en 5 secondes.");
                return 3;
            }
        }

        Console.WriteLine($"Capture {filter.SourceWidth}x{filter.SourceHeight}");
        int failures = 0;
        foreach (int pixelSize in new[] { 1, 0 })
        {
            foreach (var (machine, palette) in FilterModes.AllPalettes)
            {
                filter.Settings.PixelSize = pixelSize;
                FilterModes.ApplyPalette(filter.Settings, palette);

                filter.UseReference = true;
                double referenceMs = Measure(filter);
                var expected = filter.ReadSmall();
                filter.UseReference = false;
                double newMs = Measure(filter);
                var actual = filter.ReadSmall();

                int different = 0;
                for (int p = 0; p < expected.Length; p += 4)
                    if (expected[p] != actual[p] || expected[p + 1] != actual[p + 1] || expected[p + 2] != actual[p + 2]) different++;
                double percent = 100.0 * different / (expected.Length / 4);
                if (percent > 0.01) failures++;

                string mode = pixelSize == 0 ? "320x200" : "pixel 1";
                Console.WriteLine($"{mode,-8} {machine.Name + " " + palette.Name,-34} {different,6} px différents ({percent:F3} %)   v2 {referenceMs,6:F2} ms   nouveau {newMs,6:F2} ms");
            }
        }
        Console.WriteLine(failures == 0 ? "OK : identique à la v2 de référence (moins de 0,01 % d'écart)." : $"ÉCART : {failures} cas au-dessus de 0,01 %.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Temps moyen de la passe CGA, en millisecondes.</summary>
    static double Measure(CgaFilter filter)
    {
        filter.Apply();
        filter.WaitForGpu();
        const int runs = 100;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) filter.Apply();
        filter.WaitForGpu();
        return timer.Elapsed.TotalMilliseconds / runs;
    }

    /// <summary>Pendant 5 secondes, filtre chaque image capturée et mesure le débit et le temps de traitement.</summary>
    static int Fps(string titlePart, int priority, bool rawOnly, bool capped)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        using var device = GpuDevice.Create();
        Console.WriteLine($"priorité GPU {GpuPriority.Levels[priority].Name} : {(GpuPriority.Set(device, priority) ? "acceptée" : "refusée")}");
        using var filter = new CgaFilter(device);
        int frames = 0;
        double busyMs = 0, worstMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var capture = new WindowCapture(device, window.Handle, uncapped: !capped))
        {
            capture.FrameArrived += (texture, width, height) =>
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                if (!rawOnly)
                {
                    filter.SetSource(texture, width, height);
                    filter.Apply();
                    filter.WaitForGpu();
                }
                double ms = t.Elapsed.TotalMilliseconds;
                busyMs += ms;
                worstMs = Math.Max(worstMs, ms);
                frames++;
            };
            Console.WriteLine(capture.Uncapped ? "limite de capture levée" : "limite de capture de Windows active");
            Thread.Sleep(5000);
        }
        double seconds = clock.Elapsed.TotalSeconds;
        Console.WriteLine($"{frames / seconds:F1} images/s, traitement moyen {busyMs / Math.Max(frames, 1):F1} ms, pire {worstMs:F1} ms");
        return 0;
    }

    /// <summary>
    /// Pose l'overlay sur la fenêtre, puis vérifie qu'il lui reste « collé » :
    /// 1) une autre fenêtre (un témoin créé par le test) est au premier plan : l'overlay doit
    ///    rester visible, juste au-dessus de la fenêtre filtrée, sans être « toujours au premier
    ///    plan », et le témoin doit passer devant lui ;
    /// 2) la fenêtre filtrée repasse au premier plan : enregistre ecran.png (ce que montre
    ///    l'écran à cet endroit) et overlay.png (l'image que l'overlay croit afficher), et
    ///    vérifie que le clic au centre traverse l'overlay.
    /// </summary>
    static int OverlayTest(string titlePart, string directory)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        Directory.CreateDirectory(directory);
        ApplicationConfiguration.Initialize();
        var overlay = new OverlayForm(window.Handle, new FilterState())
        {
            InfoVisible = true,
            Hint = "Ctrl+Alt + R retirer le filtre, S image PNG",
        };
        // Témoin : une fenêtre ordinaire qui recouvre le coin haut gauche de la fenêtre filtrée.
        var witness = new Form
        {
            Text = "témoin retro-filter",
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.Yellow,
        };
        int step = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            step++;
            if (step == 1)
            {
                var client = WindowList.GetClientScreenRect(window.Handle);
                witness.Bounds = new Rectangle(client.X + 40, client.Y + 40, 300, 200);
                witness.Show();
                ForceForeground(witness.Handle);
                return;
            }
            if (step == 2)
            {
                bool visible = WindowList.IsVisible(overlay.Handle);
                bool above = WindowList.IsJustAbove(window.Handle, overlay.Handle, out _);
                bool topmost = WindowList.IsTopmost(overlay.Handle);
                var inWitness = new Point(witness.Bounds.X + 150, witness.Bounds.Y + 120);
                using (var grab = ScreenGrab(new Rectangle(inWitness, new Size(1, 1))))
                {
                    var pixel = grab.GetPixel(0, 0);
                    bool witnessInFront = pixel.R > 200 && pixel.G > 200 && pixel.B < 80;
                    Console.WriteLine($"fenêtre filtrée inactive : overlay visible = {visible}, juste au-dessus d'elle = {above}, "
                        + $"toujours au premier plan = {topmost}, autre fenêtre devant l'overlay = {witnessInFront}");
                    Console.WriteLine(visible && above && !topmost && witnessInFront ? "collage : OK" : "collage : ÉCHEC");
                    if (!above) Console.WriteLine($"overlay 0x{overlay.Handle:X}, fenêtre filtrée 0x{window.Handle:X}, au-dessus d'elle :\n{WindowList.DescribeAbove(window.Handle, 8)}");
                }
                witness.Close();
                ForceForeground(window.Handle);
                return;
            }
            {
                timer.Stop();
                Console.WriteLine($"fenêtre filtrée au premier plan = {WindowList.Foreground == window.Handle}, "
                    + $"overlay juste au-dessus d'elle = {WindowList.IsJustAbove(window.Handle, overlay.Handle, out _)}");
                var area = overlay.Bounds;
            using (var screen = ScreenGrab(area))
                screen.Save(Path.Combine(directory, "ecran.png"));
            string? saved = overlay.SaveSnapshot();
            if (saved != null) File.Copy(saved, Path.Combine(directory, "overlay.png"), true);
            Console.WriteLine($"overlay {area} -> {directory}");
            // Transparence aux clics : la fenêtre « touchée » au centre doit être le jeu, pas l'overlay.
            var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            IntPtr hit = GetAncestor(WindowFromPoint(center), GA_ROOT);
            Console.WriteLine(hit == window.Handle ? "clic au centre : reçu par la fenêtre du jeu"
                : hit == overlay.Handle ? "clic au centre : intercepté par l'overlay (ÉCHEC)"
                : "clic au centre : traverse l'overlay (le jeu est recouvert à cet endroit par une autre fenêtre)");
            Console.WriteLine(WindowList.DescribeGeometry(window.Handle));
            overlay.Close();
            }
        };
        overlay.Shown += (_, _) => timer.Start();
        Application.Run(overlay);
        return 0;
    }

    /// <summary>
    /// Pour les tests : met une fenêtre au premier plan même si Windows le refuserait à un
    /// programme lancé en arrière-plan (un appui bref sur Alt lève cette protection).
    /// </summary>
    static void ForceForeground(IntPtr hwnd)
    {
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        WindowList.Activate(hwnd);
    }

    /// <summary>
    /// Enregistre l'icône (agrandie), des captures du menu de la zone de notification avec
    /// chaque sous-menu (carte, mode, palette) déplié tour à tour, puis la fenêtre principale
    /// (fenetre.png, et fenetre-avance.png avec « Avancé » déplié).
    /// </summary>
    static int UiTest(string directory)
    {
        Directory.CreateDirectory(directory);
        ApplicationConfiguration.Initialize();
        using (var icon = RetroStyle.CreateIcon(32))
        using (var bitmap = icon.ToBitmap())
        using (var big = new Bitmap(128, 128))
        {
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(bitmap, 0, 0, 128, 128);
            }
            big.Save(Path.Combine(directory, "icone.png"));
        }

        var form = new MainForm();
        int[] submenus = [2, 3, 4];
        int step = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 700 };
        Rectangle area = Rectangle.Empty;
        timer.Tick += (_, _) =>
        {
            if (step > 0 && !area.IsEmpty)
            {
                using var shot = ScreenGrab(area);
                shot.Save(Path.Combine(directory, $"menu{step}.png"));
            }
            if (step >= submenus.Length)
            {
                // Après les menus : la fenêtre principale, sans puis avec « Avancé ».
                int extra = step - submenus.Length;
                if (extra > 0)
                {
                    using var window = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(window, new Rectangle(Point.Empty, form.Size));
                    window.Save(Path.Combine(directory, extra == 1 ? "fenetre.png" : "fenetre-avance.png"));
                }
                if (extra == 2)
                {
                    timer.Stop();
                    form.Exit();
                    return;
                }
                form.PrepareForTest(advanced: extra == 1);
                area = Rectangle.Empty;
                step++;
                return;
            }
            area = form.ShowTrayMenuForTest(new Point(200, 200), submenus[step]);
            step++;
        };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        Console.WriteLine($"interface -> {directory}");
        return 0;
    }

    /// <summary>Copie d'une zone de l'écran, fenêtres « calques » comprises (CAPTUREBLT).</summary>
    static Bitmap ScreenGrab(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(bitmap);
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr bitmapDc = g.GetHdc();
        BitBlt(bitmapDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SRCCOPY | CAPTUREBLT);
        g.ReleaseHdc(bitmapDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        return bitmap;
    }

    const uint GA_ROOT = 2;
    const byte VK_MENU = 0x12;
    const uint KEYEVENTF_KEYUP = 0x2;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    const uint SRCCOPY = 0x00CC0020;
    const uint CAPTUREBLT = 0x40000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);
}
