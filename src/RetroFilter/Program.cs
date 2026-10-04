namespace RetroFilter;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Mode test sans interface : RetroFilter --snapshot "<partie du titre>" <fichier.png>
        if (args.Length == 3 && args[0] == "--snapshot")
            return Snapshot(args[1], args[2]);

        ApplicationConfiguration.Initialize();
        Application.Run(new WindowPickerForm());
        return 0;
    }

    /// <summary>Capture une image brute de la fenêtre et l'enregistre en PNG.</summary>
    static int Snapshot(string titlePart, string path)
    {
        var window = WindowList.GetCapturableWindows()
            .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            Console.Error.WriteLine($"Aucune fenêtre ne contient « {titlePart} ».");
            return 2;
        }

        using var device = GpuDevice.Create();
        using var capture = new WindowCapture(device, window.Handle);
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        capture.FrameArrived += (texture, width, height) =>
        {
            if (done.IsSet) return;
            try
            {
                TextureSaver.SavePng(device, texture, width, height, path);
                Console.WriteLine($"{window.Title} : {width}x{height} -> {path}");
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
