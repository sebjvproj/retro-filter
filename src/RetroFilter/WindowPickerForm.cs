using System.Media;
using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>
/// Fenêtre principale. Elle enregistre les raccourcis globaux (actifs même quand le jeu a le
/// focus) et pose l'overlay sur le jeu. La liste sert à ouvrir l'aperçu de test.
/// </summary>
sealed class WindowPickerForm : Form
{
    // Raccourcis globaux : Ctrl+Alt + lettre. Lettres choisies pour ne pas produire de caractère
    // avec AltGr (= Ctrl+Alt) sur un clavier AZERTY.
    // Ctrl+Alt+M et Ctrl+Alt+G sont déjà pris par une autre application chez l'utilisateur.
    enum Hotkey { Toggle = 1, NextMode = 2, Save = 3, Info = 4 }
    static readonly (Hotkey Id, Keys Key, string Description)[] Hotkeys =
    [
        (Hotkey.Toggle, Keys.R, "Ctrl+Alt+R : filtre sur le jeu au premier plan, ou retrait du filtre"),
        (Hotkey.NextMode, Keys.T, "Ctrl+Alt+T : mode de tramage suivant (moderne, pixel 2 à 4, historique)"),
        (Hotkey.Save, Keys.S, "Ctrl+Alt+S : enregistrer l'image filtrée en PNG (Images\\retro-filter)"),
        (Hotkey.Info, Keys.I, "Ctrl+Alt+I : afficher ou cacher la barre d'informations"),
    ];

    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Button _refresh = new() { Text = "Actualiser", AutoSize = true };
    readonly Button _preview = new() { Text = "Aperçu (test)", AutoSize = true };
    readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
    OverlayForm? _overlay;
    int _mode;
    bool _infoVisible = true;
    string _hint = "";

    public WindowPickerForm()
    {
        Text = "retro-filter";
        ClientSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterScreen;

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
            Text = "Lance ton jeu en mode fenêtré ou fenêtré sans bordure, puis, dans le jeu :\n\n"
                + string.Join("\n", Hotkeys.Select(h => "    " + h.Description))
                + "\n\nAperçu de test : choisir une fenêtre dans la liste.",
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(4),
        };
        buttons.Controls.Add(_preview);
        buttons.Controls.Add(_refresh);

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(_status);
        Controls.Add(help);

        _refresh.Click += (_, _) => RefreshList();
        _preview.Click += (_, _) => OpenPreview();
        _list.DoubleClick += (_, _) => OpenPreview();
        Activated += (_, _) => { if (_list.Items.Count == 0) RefreshList(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var refused = Hotkeys.Where(h => !RegisterHotKey(Handle, (int)h.Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)h.Key)).ToList();
        _status.Text = refused.Count == 0
            ? "Raccourcis actifs."
            : "Raccourcis déjà pris par une autre application : " + string.Join(", ", refused.Select(h => "Ctrl+Alt+" + h.Key));
        _hint = "Ctrl+Alt + T mode, S image PNG, I infos, R retirer le filtre";
        if (refused.Count > 0)
            _hint += "   (inactifs : " + string.Join(", ", refused.Select(h => h.Key)) + ")";
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
            OnHotkey((Hotkey)(int)m.WParam);
        base.WndProc(ref m);
    }

    void OnHotkey(Hotkey hotkey)
    {
        switch (hotkey)
        {
            case Hotkey.Toggle:
                if (_overlay != null) _overlay.Close();
                else Attach(WindowList.Foreground);
                break;
            case Hotkey.NextMode:
                if (_overlay == null) break;
                _mode = _overlay.NextMode();
                _status.Text = $"Mode : {FilterModes.All[_mode].Name}";
                _overlay.ShowMessage(_status.Text);
                break;
            case Hotkey.Info:
                _infoVisible = !_infoVisible;
                if (_overlay != null) _overlay.InfoVisible = _infoVisible;
                break;
            case Hotkey.Save:
                if (_overlay == null) break;
                try
                {
                    string? path = _overlay.SaveSnapshot();
                    _status.Text = path == null ? "Aucune image capturée pour l'instant." : $"Enregistré : {path}";
                    _overlay.ShowMessage(path == null ? "Aucune image capturée pour l'instant" : $"Image enregistrée : {Path.GetFileName(path)}");
                    if (path != null) SystemSounds.Asterisk.Play();
                }
                catch (Exception ex)
                {
                    _status.Text = $"Échec de l'enregistrement : {ex.Message}";
                    _overlay.ShowMessage(_status.Text);
                }
                break;
        }
    }

    /// <summary>Pose l'overlay sur la fenêtre donnée, si c'est une fenêtre qu'on sait capturer.</summary>
    void Attach(IntPtr hwnd)
    {
        var game = WindowList.GetCapturableWindows().FirstOrDefault(w => w.Handle == hwnd);
        if (game == null)
        {
            SystemSounds.Beep.Play();
            return;
        }

        try
        {
            _overlay = new OverlayForm(game.Handle, _mode) { InfoVisible = _infoVisible, Hint = _hint };
            _overlay.FormClosed += (_, _) =>
            {
                _overlay = null;
                _status.Text = "Filtre retiré.";
            };
            _overlay.Show();
            _status.Text = $"Filtre posé sur : {game.Title}";
        }
        catch (Exception ex)
        {
            _overlay = null;
            _status.Text = $"Capture impossible : {ex.Message}";
            SystemSounds.Beep.Play();
        }
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

    void OpenPreview()
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var h in Hotkeys) UnregisterHotKey(Handle, (int)h.Id);
        _overlay?.Close();
        base.OnFormClosed(e);
    }

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1;
    const uint MOD_CONTROL = 0x2;
    const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
