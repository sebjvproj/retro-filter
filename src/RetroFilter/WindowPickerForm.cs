using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>
/// Fenêtre principale. Elle enregistre les raccourcis globaux (actifs même quand le jeu a le
/// focus), pose l'overlay sur le jeu et porte l'icône de la zone de notification. Fermer la
/// fenêtre la cache seulement : l'app continue dans la zone de notification.
/// La liste sert à ouvrir l'aperçu de test.
/// </summary>
sealed class WindowPickerForm : Form
{
    // Raccourcis globaux : Ctrl+Alt + lettre. Lettres choisies pour ne pas produire de caractère
    // avec AltGr (= Ctrl+Alt) sur un clavier AZERTY.
    // Ctrl+Alt+M et Ctrl+Alt+G sont déjà pris par une autre application chez l'utilisateur.
    enum Hotkey { Toggle = 1, NextMode = 2, Save = 3, Info = 4, NextPalette = 5, NextMachine = 6 }
    static readonly (Hotkey Id, Keys Key, string Description)[] Hotkeys =
    [
        (Hotkey.Toggle, Keys.R, "Ctrl+Alt+R : filtre sur le jeu au premier plan, ou retrait du filtre"),
        (Hotkey.NextMode, Keys.T, "Ctrl+Alt+T : mode de tramage suivant (moderne, pixel 2 à 4, historique)"),
        (Hotkey.NextMachine, Keys.C, "Ctrl+Alt+C : carte graphique suivante (CGA, EGA)"),
        (Hotkey.NextPalette, Keys.P, "Ctrl+Alt+P : palette suivante"),
        (Hotkey.Save, Keys.S, "Ctrl+Alt+S : enregistrer l'image filtrée en PNG (Images\\retro-filter)"),
        (Hotkey.Info, Keys.I, "Ctrl+Alt+I : afficher ou cacher la barre d'informations"),
    ];

    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly Button _refresh = new() { Text = "Actualiser", AutoSize = true };
    readonly Button _preview = new() { Text = "Aperçu (test)", AutoSize = true };
    readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
    readonly NotifyIcon _tray = new();
    readonly ContextMenuStrip _trayMenu = new();
    readonly AppSettings _settings = AppSettings.Load();
    readonly FilterState _state = new();
    OverlayForm? _overlay;
    string _hint = "";
    bool _exiting;

    public WindowPickerForm()
    {
        Text = "retro-filter";
        Icon = RetroStyle.CreateIcon(32);
        ClientSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterScreen;
        _state.Load(_settings);

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
            Text = "Lance ton jeu en mode fenêtré ou fenêtré sans bordure, puis, dans le jeu :\n\n"
                + string.Join("\n", Hotkeys.Select(h => "    " + h.Description))
                + "\n\nRéglages : clic droit sur l'icône retro-filter de la zone de notification."
                + "\nAperçu de test : choisir une fenêtre dans la liste.",
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

        ToolStripManager.Renderer = new RetroStyle.MenuRenderer();
        _trayMenu.Opening += (_, _) => BuildTrayMenu();
        _tray.Icon = RetroStyle.CreateIcon(16);
        _tray.Text = "retro-filter";
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.Visible = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var refused = Hotkeys.Where(h => !RegisterHotKey(Handle, (int)h.Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)h.Key)).ToList();
        _status.Text = refused.Count == 0
            ? "Raccourcis actifs."
            : "Raccourcis déjà pris par une autre application : " + string.Join(", ", refused.Select(h => "Ctrl+Alt+" + h.Key));
        _hint = "Ctrl+Alt + C carte, T mode, P palette, S image PNG, I infos, R retirer le filtre";
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
            case Hotkey.NextMachine:
                ChangeState(_state.NextMachine);
                break;
            case Hotkey.NextMode:
                ChangeState(_state.NextMode);
                break;
            case Hotkey.NextPalette:
                ChangeState(_state.NextPalette);
                break;
            case Hotkey.Info:
                SetInfoBar(!_settings.InfoBar);
                break;
            case Hotkey.Save:
                SaveSnapshot();
                break;
        }
    }

    /// <summary>Change la carte, le mode ou la palette, l'applique au jeu et le mémorise.</summary>
    void ChangeState(Action change)
    {
        change();
        _state.Save(_settings);
        _settings.Save();
        _status.Text = _state.Description;
        if (_overlay == null) return;
        _overlay.ApplyState();
        _overlay.ShowMessage(_state.Description);
    }

    void SetInfoBar(bool visible)
    {
        _settings.InfoBar = visible;
        _settings.Save();
        if (_overlay != null) _overlay.InfoVisible = visible;
    }

    void SaveSnapshot()
    {
        if (_overlay == null) return;
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
            _overlay = new OverlayForm(game.Handle, _state) { InfoVisible = _settings.InfoBar, Hint = _hint };
            _overlay.FormClosed += (_, _) =>
            {
                _overlay = null;
                _status.Text = "Filtre retiré.";
            };
            _overlay.Show();
            _overlay.ShowMessage($"{_state.Description}. Ctrl+Alt+R pour retirer le filtre", 4);
            _status.Text = $"Filtre posé sur : {game.Title}";
        }
        catch (Exception ex)
        {
            _overlay = null;
            _status.Text = $"Capture impossible : {ex.Message}";
            SystemSounds.Beep.Play();
        }
    }

    // --- Zone de notification ---

    /// <summary>Menu reconstruit à chaque ouverture, pour refléter le choix courant.</summary>
    void BuildTrayMenu()
    {
        _trayMenu.Items.Clear();

        string status = _overlay != null ? "Filtre actif" : "Filtre inactif : Ctrl+Alt+R dans le jeu";
        _trayMenu.Items.Add(new ToolStripMenuItem(status) { Enabled = false });
        _trayMenu.Items.Add(new ToolStripSeparator());

        var machines = new ToolStripMenuItem($"Carte : {_state.Machine.Name}");
        for (int i = 0; i < FilterModes.Machines.Length; i++)
        {
            int index = i;
            machines.DropDownItems.Add(new ToolStripMenuItem(FilterModes.Machines[i].Name, null,
                (_, _) => ChangeState(() => _state.SetMachine(index))) { Checked = i == _state.MachineIndex });
        }
        _trayMenu.Items.Add(machines);

        // Mode : moderne et historique en premier ; les tailles de pixel intermédiaires dans « Avancé ».
        var modes = new ToolStripMenuItem($"Mode : {_state.Mode.Name}");
        var advancedModes = new ToolStripMenuItem("Avancé");
        for (int i = 0; i < FilterModes.All.Length; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(FilterModes.All[i].Name, null,
                (_, _) => ChangeState(() => _state.SetMode(index))) { Checked = i == _state.ModeIndex };
            if (FilterModes.All[i].PixelSize is 0 or 1) modes.DropDownItems.Add(item);
            else advancedModes.DropDownItems.Add(item);
        }
        modes.DropDownItems.Add(new ToolStripSeparator());
        modes.DropDownItems.Add(advancedModes);
        _trayMenu.Items.Add(modes);

        var palettes = new ToolStripMenuItem($"Palette : {_state.Palette.Name}");
        for (int i = 0; i < _state.Machine.Palettes.Length; i++)
        {
            int index = i;
            var palette = _state.Machine.Palettes[i];
            palettes.DropDownItems.Add(new ToolStripMenuItem(palette.Name, RetroStyle.CreateSwatch(palette),
                (_, _) => ChangeState(() => _state.SetPalette(index)))
            {
                Checked = i == _state.PaletteIndex,
                ImageScaling = ToolStripItemImageScaling.None, // pastille 32x16 à sa taille réelle
            });
        }
        _trayMenu.Items.Add(palettes);

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("Retirer le filtre", null, (_, _) => _overlay?.Close()) { Enabled = _overlay != null });
        _trayMenu.Items.Add(new ToolStripMenuItem("Ouvrir le dossier des images", null, (_, _) => OpenSnapshotFolder()));

        var advanced = new ToolStripMenuItem("Avancé");
        advanced.DropDownItems.Add(new ToolStripMenuItem("Barre d'informations (Ctrl+Alt+I)", null,
            (_, _) => SetInfoBar(!_settings.InfoBar)) { Checked = _settings.InfoBar });
        advanced.DropDownItems.Add(new ToolStripMenuItem("Fenêtre principale", null, (_, _) => ShowMainWindow()));
        _trayMenu.Items.Add(advanced);

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) => Exit()));
    }

    /// <summary>
    /// Pour les tests : ouvre le menu de la zone de notification à l'écran, avec le sous-menu
    /// donné déplié ; renvoie la zone couverte (menu + sous-menu).
    /// </summary>
    internal Rectangle ShowTrayMenuForTest(Point location, int submenu)
    {
        _trayMenu.Show(location);
        var item = (ToolStripMenuItem)_trayMenu.Items[submenu];
        item.ShowDropDown();
        return Rectangle.Union(_trayMenu.Bounds, item.DropDown.Bounds);
    }

    void ShowMainWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    static void OpenSnapshotFolder()
    {
        Directory.CreateDirectory(FilterModes.SnapshotDirectory);
        Process.Start(new ProcessStartInfo(FilterModes.SnapshotDirectory) { UseShellExecute = true });
    }

    internal void Exit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // La croix cache la fenêtre : l'app continue (raccourcis, filtre) dans la zone de notification.
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _tray.ShowBalloonTip(5000, "retro-filter continue en arrière-plan",
                    "Ctrl+Alt+R dans un jeu pour le filtre. Clic droit sur cette icône pour les réglages ou pour quitter.",
                    ToolTipIcon.None);
                _settings.TrayHintShown = true;
                _settings.Save();
            }
            return;
        }
        base.OnFormClosing(e);
    }

    // --- Aperçu de test ---

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
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1;
    const uint MOD_CONTROL = 0x2;
    const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
