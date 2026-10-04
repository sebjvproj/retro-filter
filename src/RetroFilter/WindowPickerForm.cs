namespace RetroFilter;

/// <summary>Fenêtre principale : choix de la fenêtre du jeu à capturer.</summary>
sealed class WindowPickerForm : Form
{
    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Button _refresh = new() { Text = "Actualiser", AutoSize = true };
    readonly Button _capture = new() { Text = "Capturer", AutoSize = true };

    public WindowPickerForm()
    {
        Text = "retro-filter";
        ClientSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var label = new Label
        {
            Text = "Choisir la fenêtre du jeu (en mode fenêtré ou fenêtré sans bordure) :",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(4),
        };
        buttons.Controls.Add(_capture);
        buttons.Controls.Add(_refresh);

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(label);
        AcceptButton = _capture;

        _refresh.Click += (_, _) => RefreshList();
        _capture.Click += (_, _) => StartCapture();
        _list.DoubleClick += (_, _) => StartCapture();
        Activated += (_, _) => { if (_list.Items.Count == 0) RefreshList(); };
    }

    void RefreshList()
    {
        var selected = (_list.SelectedItem as WindowInfo)?.Handle;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var window in WindowList.GetCapturableWindows())
        {
            _list.Items.Add(window);
            if (window.Handle == selected) _list.SelectedItem = window;
        }
        _list.EndUpdate();
    }

    void StartCapture()
    {
        if (_list.SelectedItem is not WindowInfo window) return;
        if (!WindowList.IsAlive(window.Handle))
        {
            RefreshList();
            return;
        }

        try
        {
            var preview = new PreviewForm(window);
            preview.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Capture impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
