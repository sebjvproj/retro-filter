namespace RetroFilter;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Mode test sans interface : RetroFilter --snapshot "<partie du titre>" <dossier> [taille de pixel]
        if (args.Length is 3 or 4 && args[0] == "--snapshot")
            return Snapshot(args[1], args[2], args.Length == 4 ? int.Parse(args[3]) : 1);

        // Mesure du débit de capture avec filtre : RetroFilter --fps "<partie du titre>" <0|1 priorité haute>
        if (args.Length == 3 && args[0] == "--fps")
            return Fps(args[1], args[2] == "1");

        // Mesure du temps GPU de la passe CGA : RetroFilter --bench "<partie du titre>"
        if (args.Length == 2 && args[0] == "--bench")
            return Bench(args[1]);

        ApplicationConfiguration.Initialize();
        Application.Run(new WindowPickerForm());
        return 0;
    }

    /// <summary>
    /// Capture une image de la fenêtre et enregistre dans le dossier : source.png (brute),
    /// cga-small.png (sortie de la passe CGA) et cga.png (agrandie à la taille de la capture).
    /// </summary>
    static int Snapshot(string titlePart, string directory, int pixelSize)
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
            filter.Apply();
            filter.WaitForGpu();
            const int runs = 100;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) filter.Apply();
            filter.WaitForGpu();
            double ms = timer.Elapsed.TotalMilliseconds / runs;
            string name = pixelSize == 0 ? "historique 320x200" : $"pixel {pixelSize}";
            Console.WriteLine($"{name,-20} {ms,7:F2} ms par image");
        }
        return 0;
    }

    /// <summary>Pendant 5 secondes, filtre chaque image capturée et mesure le débit et le temps de traitement.</summary>
    static int Fps(string titlePart, bool raisePriority)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        using var device = GpuDevice.Create();
        if (raisePriority) Console.WriteLine(GpuPriority.Raise(device));
        using var filter = new CgaFilter(device);
        int frames = 0;
        double busyMs = 0, worstMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var capture = new WindowCapture(device, window.Handle))
        {
            capture.FrameArrived += (texture, width, height) =>
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                filter.SetSource(texture, width, height);
                filter.Apply();
                filter.WaitForGpu();
                double ms = t.Elapsed.TotalMilliseconds;
                busyMs += ms;
                worstMs = Math.Max(worstMs, ms);
                frames++;
            };
            Thread.Sleep(5000);
        }
        double seconds = clock.Elapsed.TotalSeconds;
        Console.WriteLine($"{frames / seconds:F1} images/s, traitement moyen {busyMs / Math.Max(frames, 1):F1} ms, pire {worstMs:F1} ms");
        return 0;
    }
}
