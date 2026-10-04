namespace RetroFilter;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Mode test sans interface : RetroFilter --snapshot "<partie du titre>" <dossier> [taille de pixel]
        if (args.Length is 3 or 4 && args[0] == "--snapshot")
            return Snapshot(args[1], args[2], args.Length == 4 ? int.Parse(args[3]) : 0);

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
}
