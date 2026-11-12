using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;

namespace RetroFilter;

/// <summary>
/// Fenêtre principale, pensée pour un débutant : 1) choisir la fenêtre à filtrer (jeu, vidéo,
/// page web...), 2) choisir le style (carte, mode, palette), 3) un gros bouton pour poser le
/// filtre. Tout le reste est derrière « Avancé ».
/// Elle enregistre aussi les raccourcis globaux, pose l'overlay et porte l'icône de la zone de
/// notification. La croix la cache seulement : l'app continue dans la zone de notification.
/// </summary>
sealed class MainForm : Form
{
    // Raccourcis globaux : Ctrl+Alt + lettre. Lettres choisies pour ne pas produire de caractère
    // avec AltGr (= Ctrl+Alt) sur un clavier AZERTY.
    // Ctrl+Alt+M et Ctrl+Alt+G sont déjà pris par une autre application chez l'utilisateur.
    // Seul Ctrl+Alt+R est réservé en permanence. Les autres ne le sont que pendant qu'un filtre
    // est posé, pour ne pas les retirer aux autres applications ; les « Extra » demandent en plus
    // l'option « raccourcis supplémentaires » (Avancé).
    enum Hotkey { Toggle = 1, NextMode = 2, Save = 3, Info = 4, NextPalette = 5, NextMachine = 6 }
    static readonly (Hotkey Id, Keys Key, bool Extra, string Hint)[] Hotkeys =
    [
        (Hotkey.Toggle, Keys.R, false, L.T("R retirer le filtre", "R remove the filter")),
        (Hotkey.Save, Keys.S, false, L.T("S image PNG", "S PNG image")),
        (Hotkey.NextMachine, Keys.C, true, L.T("C carte", "C card")),
        (Hotkey.NextMode, Keys.T, true, "T mode"),
        (Hotkey.NextPalette, Keys.P, true, "P palette"),
        (Hotkey.Info, Keys.I, true, L.T("I infos", "I info")),
    ];

    /// <summary>Largeur du contenu de la fenêtre, à 100 %.</summary>
    const int ContentWidth = 620;

    readonly ListBox _list = new();
    readonly FlowLayoutPanel _machines = NewRow(), _modes = NewRow(), _palettes = NewRow(), _pixelSizes = NewRow();
    readonly Label _machineDetail = NewDetail(), _modeDetail = NewDetail(), _detail = NewDetail();
    readonly Button _apply = new();
    readonly Label _status = new();
    readonly Label _shortcuts = new();
    readonly LinkLabel _advancedLink = new();
    readonly TableLayoutPanel _advanced = new();
    readonly CheckBox _extraHotkeys = new(), _infoBar = new();
    readonly ToolTip _tips = new();
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1500 };
    readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 4000 };
    readonly InfoBarForm _toast = new(topmost: true);
    readonly Dictionary<IntPtr, Bitmap?> _icons = new();
    readonly HashSet<Hotkey> _registered = new(), _refused = new();
    readonly NotifyIcon _tray = new();
    readonly ContextMenuStrip _trayMenu = new();
    readonly AppSettings _settings = AppSettings.Load();
    readonly FilterState _state = new();
    OverlayForm? _overlay;
    bool _exiting;
    bool _selectingZone;
    bool _demoList;

    public MainForm()
    {
        Text = "retro-filter";
        Icon = RetroStyle.CreateIcon(32);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = RetroStyle.Black;
        ForeColor = RetroStyle.White;
        Font = RetroStyle.TextFont;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _state.Load(_settings);

        BuildLayout();
        BuildChoices();
        RefreshChoices();
        UpdateFilterState();
        Notify(L.T("Choisis une fenêtre dans la liste, puis « Poser le filtre ».", "Pick a window in the list, then \"Apply the filter\"."));

        _refresh.Tick += (_, _) => { if (Visible) RefreshList(); };
        _refresh.Start();
        Shown += (_, _) => RefreshList();
        VisibleChanged += (_, _) => { if (Visible) RefreshList(); };

        _ = _toast.Handle; // créée maintenant, affichée par Notify()
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.Place(Point.Empty, false);
        };

        ToolStripManager.Renderer = new RetroStyle.MenuRenderer();
        _trayMenu.Opening += (_, _) => BuildTrayMenu();
        _tray.Icon = RetroStyle.CreateIcon(16);
        _tray.Text = "retro-filter";
        _tray.ContextMenuStrip = _trayMenu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMainWindow(); };
        _tray.Visible = true;
    }

    // --- Mise en page ---

    /// <summary>Taille en pixels pour une valeur donnée à 100 % (96 points par pouce).</summary>
    int S(int value) => value * DeviceDpi / 96;

    static FlowLayoutPanel NewRow() => new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };

    /// <summary>Explication en gris à côté ou en dessous d'un choix.</summary>
    static Label NewDetail() => new() { AutoSize = true, ForeColor = RetroStyle.Gray, Anchor = AnchorStyles.Left, Margin = Padding.Empty };

    Label Heading(string text) => new()
    {
        Text = text,
        Font = RetroStyle.BoldFont,
        ForeColor = RetroStyle.Magenta,
        AutoSize = true,
        Margin = new Padding(0, S(14), 0, S(6)),
    };

    Label Note(string text) => new()
    {
        Text = text,
        ForeColor = RetroStyle.Gray,
        AutoSize = true,
        MaximumSize = new Size(S(ContentWidth), 0),
        Margin = new Padding(0, S(4), 0, 0),
    };

    void BuildLayout()
    {
        int width = S(ContentWidth);
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(S(18), S(10), S(18), S(14)),
        };

        root.Controls.Add(new Label
        {
            Text = "RETRO-FILTER",
            Font = RetroStyle.TitleFont,
            ForeColor = RetroStyle.Cyan,
            AutoSize = true,
            Margin = Padding.Empty,
        });
        root.Controls.Add(Note(L.T("N'importe quelle fenêtre avec les couleurs des PC des années 80.", "Any window, in the colors of a 1980s PC.")));

        // 1. La fenêtre à filtrer
        root.Controls.Add(Heading(L.T("1. QUE VEUX-TU FILTRER ?", "1. WHAT DO YOU WANT TO FILTER?")));
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = S(26);
        _list.IntegralHeight = false;
        _list.Size = new Size(width, _list.ItemHeight * 6 + 4);
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = RetroStyle.Black;
        _list.ForeColor = RetroStyle.White;
        _list.Margin = Padding.Empty;
        _list.DrawItem += DrawListItem;
        _list.DoubleClick += (_, _) => ApplyClicked();
        root.Controls.Add(_list);
        root.Controls.Add(Note(L.T("Un jeu (fenêtré ou fenêtré sans bordure), une vidéo, une page web...", "A game (windowed or borderless windowed), a video, a web page...")));

        var scope = NewRow();
        scope.Margin = new Padding(0, S(8), 0, 0);
        AddChoice(scope, L.T("Toute la fenêtre", "Whole window"), L.T("Le filtre couvre toute la fenêtre choisie", "The filter covers the whole window you picked"), () => !_settings.ZoneOnly, () => SetZoneOnly(false));
        AddChoice(scope, L.T("Seulement une zone", "One area only"), L.T("Tu traces un rectangle à la souris et seul ce rectangle est filtré : une vidéo dans une page web, par exemple", "You draw a rectangle with the mouse and only that rectangle is filtered: a video on a web page, for example"),
            () => _settings.ZoneOnly, () => SetZoneOnly(true));
        root.Controls.Add(scope);

        // 2. Le style
        root.Controls.Add(Heading(L.T("2. CHOISIS TON STYLE", "2. PICK YOUR STYLE")));
        var choices = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        foreach (var (name, row) in new[] { (L.T("Carte", "Card"), _machines), ("Mode", _modes), ("Palette", _palettes) })
        {
            choices.Controls.Add(new Label
            {
                Text = name,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, S(12), 0),
            });
            row.Margin = new Padding(0, S(3), 0, S(3));
            choices.Controls.Add(row);
        }
        // Sous les pastilles : le nom de la palette choisie.
        choices.Controls.Add(new Label { AutoSize = true });
        choices.Controls.Add(_detail);
        root.Controls.Add(choices);

        // 3. Le bouton
        _apply.Font = RetroStyle.BigFont;
        _apply.FlatStyle = FlatStyle.Flat;
        _apply.Size = new Size(width, S(48));
        _apply.Margin = new Padding(0, S(12), 0, S(6));
        _apply.Cursor = Cursors.Hand;
        _apply.FlatAppearance.BorderSize = 2;
        _apply.Click += (_, _) => ApplyClicked();
        root.Controls.Add(_apply);

        _status.AutoSize = false;
        _status.Size = new Size(width, RetroStyle.TextFont.Height * 2 + 2);
        _status.ForeColor = RetroStyle.Gray;
        _status.Margin = Padding.Empty;
        root.Controls.Add(_status);

        _shortcuts.AutoSize = true;
        _shortcuts.MaximumSize = new Size(width, 0);
        _shortcuts.Margin = new Padding(0, S(6), 0, 0);
        root.Controls.Add(_shortcuts);

        // Avancé
        _advancedLink.Text = L.T("+ Avancé", "+ Advanced");
        _advancedLink.AutoSize = true;
        _advancedLink.LinkColor = RetroStyle.Gray;
        _advancedLink.ActiveLinkColor = RetroStyle.Cyan;
        _advancedLink.LinkBehavior = LinkBehavior.HoverUnderline;
        _advancedLink.Margin = new Padding(0, S(12), 0, 0);
        _advancedLink.LinkClicked += (_, _) => SetAdvancedVisible(!_advanced.Visible);
        root.Controls.Add(_advancedLink);

        _advanced.AutoSize = true;
        _advanced.ColumnCount = 1;
        _advanced.Visible = false;
        _advanced.Margin = new Padding(0, S(6), 0, 0);

        var pixelRow = NewRow();
        pixelRow.Controls.Add(new Label { Text = L.T("Gros pixels", "Chunky pixels"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(12), 0) });
        pixelRow.Controls.Add(_pixelSizes);
        _advanced.Controls.Add(pixelRow);

        StyleCheckBox(_extraHotkeys, L.T("Raccourcis en plus : Ctrl+Alt+C carte, T mode, P palette, I infos", "More shortcuts: Ctrl+Alt+C card, T mode, P palette, I info"));
        _extraHotkeys.Checked = _settings.ExtraHotkeys;
        _extraHotkeys.CheckedChanged += (_, _) =>
        {
            _settings.ExtraHotkeys = _extraHotkeys.Checked;
            _settings.Save();
            UpdateHotkeys();
        };
        _advanced.Controls.Add(_extraHotkeys);

        StyleCheckBox(_infoBar, L.T("Barre d'informations (images par seconde, temps de traitement)", "Info bar (frames per second, processing time)"));
        _infoBar.Checked = _settings.InfoBar;
        _infoBar.CheckedChanged += (_, _) => SetInfoBar(_infoBar.Checked);
        _advanced.Controls.Add(_infoBar);

        var languageRow = NewRow();
        languageRow.Margin = new Padding(0, S(8), 0, 0);
        languageRow.Controls.Add(new Label { Text = L.T("Langue", "Language"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(12), 0) });
        foreach (var (name, code) in new[] { ("Français", "fr"), ("English", "en") })
            AddChoice(languageRow, name, name, () => (_settings.Language ?? L.Code) == code, () => SetLanguage(code));
        _advanced.Controls.Add(languageRow);

        var tools = NewRow();
        tools.Margin = new Padding(0, S(6), 0, 0);
        tools.Controls.Add(Choice(L.T("Dossier des images", "Image folder"), L.T("Ouvre le dossier où Ctrl+Alt+S enregistre les images", "Opens the folder where Ctrl+Alt+S saves the images"), false, OpenSnapshotFolder));
        tools.Controls.Add(Choice(L.T("Aperçu de test", "Test preview"), L.T("Affiche la fenêtre choisie, filtrée, dans une fenêtre à part (outil de mise au point)", "Shows the picked window, filtered, in a separate window (a debugging tool)"), false, OpenPreview));
        _advanced.Controls.Add(tools);
        root.Controls.Add(_advanced);

        Controls.Add(root);
    }

    void StyleCheckBox(CheckBox box, string text)
    {
        box.Text = text;
        box.AutoSize = true;
        box.MaximumSize = new Size(S(ContentWidth), 0);
        box.ForeColor = RetroStyle.White;
        box.Margin = new Padding(0, S(4), 0, 0);
    }

    void SetAdvancedVisible(bool visible)
    {
        _advanced.Visible = visible;
        _advancedLink.Text = visible ? L.T("- Avancé", "- Advanced") : L.T("+ Avancé", "+ Advanced");
    }

    /// <summary>Bouton de choix : cyan quand il est sélectionné.</summary>
    Button Choice(string text, string tip, bool selected, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(S(6), S(1), S(6), S(1)),
            Margin = new Padding(0, 0, S(6), 0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.MouseDownBackColor = RetroStyle.Cyan;
        StyleChoice(button, selected);
        button.Click += (_, _) => click();
        _tips.SetToolTip(button, tip);
        return button;
    }

    static void StyleChoice(Button button, bool selected)
    {
        var back = selected ? RetroStyle.Cyan : RetroStyle.Black;
        if (button.BackColor == back && button.FlatAppearance.BorderColor != Color.Empty) return;
        button.BackColor = back;
        button.ForeColor = selected ? Color.Black : RetroStyle.White;
        button.FlatAppearance.BorderColor = selected ? RetroStyle.Cyan : RetroStyle.Gray;
        button.FlatAppearance.MouseOverBackColor = selected ? RetroStyle.Cyan : RetroStyle.DarkGray;
    }

    /// <summary>Pastille de palette cliquable : cadre magenta quand elle est sélectionnée.</summary>
    Button Swatch(PaletteChoice palette, Action click)
    {
        var image = RetroStyle.CreateSwatch(palette, 2);
        var button = new Button
        {
            Image = image,
            Size = new Size(image.Width + 12, image.Height + 12),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, S(6), 0),
            BackColor = RetroStyle.Black,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = RetroStyle.Magenta;
        button.FlatAppearance.MouseOverBackColor = RetroStyle.DarkGray;
        button.Click += (_, _) => click();
        _tips.SetToolTip(button, $"{palette.Name} ({palette.Detail})");
        return button;
    }

    static void StyleSwatch(Button button, bool selected)
    {
        var back = selected ? RetroStyle.Magenta : RetroStyle.Black;
        if (button.BackColor == back) return;
        button.BackColor = back;
        button.FlatAppearance.MouseOverBackColor = selected ? RetroStyle.Magenta : RetroStyle.DarkGray;
    }

    // Boutons créés une seule fois ; un changement de choix ne fait que les recolorer
    // (les recréer à chaque clic faisait clignoter la fenêtre).
    readonly List<(Button Button, Func<bool> Selected)> _choiceButtons = new();
    readonly List<(Button Button, Func<bool> Selected)> _swatchButtons = new();
    readonly List<FlowLayoutPanel> _paletteRows = new();

    /// <summary>Ajoute à une rangée un bouton de choix, recoloré ensuite par RefreshChoices().</summary>
    void AddChoice(FlowLayoutPanel row, string text, string tip, Func<bool> selected, Action click)
    {
        var button = Choice(text, tip, selected(), click);
        _choiceButtons.Add((button, selected));
        row.Controls.Add(button);
    }

    /// <summary>La langue est lue au démarrage : le changement vaut pour le prochain lancement.</summary>
    void SetLanguage(string code)
    {
        _settings.Language = code;
        _settings.Save();
        RefreshChoices();
        Notify(code == L.Code
            ? L.T("Langue : français.", "Language: English.")
            : code == "fr" ? "Français au prochain lancement de retro-filter." : "English the next time you start retro-filter.");
    }

    void SetZoneOnly(bool zoneOnly)
    {
        _settings.ZoneOnly = zoneOnly;
        _settings.Save();
        RefreshChoices();
        if (_overlay != null) Notify(L.T("Ce choix s'appliquera au prochain filtre posé.", "This choice applies to the next filter you apply."));
        else Notify(zoneOnly ? L.T("Après « Poser le filtre », trace la zone à la souris sur la fenêtre.", "After \"Apply the filter\", draw the area on the window with the mouse.") : L.T("Le filtre couvrira toute la fenêtre.", "The filter will cover the whole window."));
    }

    /// <summary>Crée les boutons de carte, mode et palette (une rangée de pastilles par carte).</summary>
    void BuildChoices()
    {

        for (int i = 0; i < FilterModes.Machines.Length; i++)
        {
            int index = i;
            var machine = FilterModes.Machines[i];
            AddChoice(_machines, machine.Name, $"{machine.Name} : {machine.Detail}", () => _state.MachineIndex == index,
                () => ChangeState(() => _state.SetMachine(index)));

            // Les pastilles de cette carte ; seule la rangée de la carte courante est visible.
            var row = NewRow();
            for (int j = 0; j < machine.Palettes.Length; j++)
            {
                int paletteIndex = j;
                var button = Swatch(machine.Palettes[j], () => ChangeState(() => _state.SetPalette(paletteIndex)));
                _swatchButtons.Add((button, () => _state.MachineIndex == index && _state.PaletteIndex == paletteIndex));
                row.Controls.Add(button);
            }
            _paletteRows.Add(row);
            _palettes.Controls.Add(row);
        }

        for (int i = 0; i < FilterModes.All.Length; i++)
        {
            int index = i;
            int pixelSize = FilterModes.All[i].PixelSize;
            var row = pixelSize is 0 or 1 ? _modes : _pixelSizes;
            string text = pixelSize == 1 ? L.T("Moderne", "Modern") : pixelSize == 0 ? L.T("Historique", "Period") : $"x{pixelSize}";
            AddChoice(row, text, ModeDetail(pixelSize), () => _state.ModeIndex == index, () => ChangeState(() => _state.SetMode(index)));
        }

        // L'explication du choix courant, à droite des boutons (sous les pastilles pour la palette).
        _machines.Controls.Add(_machineDetail);
        _modes.Controls.Add(_modeDetail);
    }

    /// <summary>Met les boutons de carte, mode et palette en accord avec le choix courant.</summary>
    void RefreshChoices()
    {
        SuspendLayout();
        foreach (var (button, selected) in _choiceButtons) StyleChoice(button, selected());
        foreach (var (button, selected) in _swatchButtons) StyleSwatch(button, selected());
        for (int i = 0; i < _paletteRows.Count; i++) _paletteRows[i].Visible = i == _state.MachineIndex;
        _machineDetail.Text = _state.Machine.Detail;
        _modeDetail.Text = ModeDetail(_state.Mode.PixelSize);
        _detail.Text = $"{_state.Palette.Name} ({_state.Palette.Detail})";
        ResumeLayout();
    }

    static string ModeDetail(int pixelSize) => pixelSize switch
    {
        1 => L.T("tramage fin, à la résolution de l'écran", "fine dithering, at screen resolution"),
        0 => L.T("320x200, gros pixels comme à l'époque", "320x200, chunky pixels like the real thing"),
        _ => L.T($"gros pixels x{pixelSize} (Avancé)", $"chunky pixels x{pixelSize} (Advanced)"),
    };

    // --- Liste des fenêtres ---

    void RefreshList()
    {
        if (_demoList) return;
        var windows = WindowList.GetCapturableWindows();
        var current = _list.Items.Cast<WindowInfo>().ToList();
        if (windows.SequenceEqual(current)) return;

        var selected = (_list.SelectedItem as WindowInfo)?.Handle;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var window in windows)
        {
            _list.Items.Add(window);
            if (window.Handle == selected) _list.SelectedItem = window;
        }
        // Par défaut : la fenêtre utilisée le plus récemment (la liste suit l'ordre d'empilement).
        if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();

        foreach (var gone in _icons.Keys.Where(h => windows.All(w => w.Handle != h)).ToList())
        {
            _icons[gone]?.Dispose();
            _icons.Remove(gone);
        }
    }

    Bitmap? IconFor(IntPtr hwnd)
    {
        if (!_icons.TryGetValue(hwnd, out var icon))
            _icons[hwnd] = icon = WindowList.GetIcon(hwnd);
        return icon;
    }

    void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var window = (WindowInfo)_list.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool filtered = _overlay?.Game == window.Handle;
        var bounds = e.Bounds;

        using (var background = new SolidBrush(selected ? RetroStyle.Cyan : RetroStyle.Black))
            e.Graphics.FillRectangle(background, bounds);

        int iconSize = S(16);
        if (IconFor(window.Handle) is Bitmap icon)
            e.Graphics.DrawImage(icon, bounds.X + S(6), bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);

        // À droite : « FILTRE » sur la fenêtre filtrée, sinon le nom du programme.
        string tag = filtered ? L.T("FILTRE", "FILTER") : window.ProcessName;
        var tagFont = filtered ? RetroStyle.BoldFont : RetroStyle.TextFont;
        var tagColor = filtered ? (selected ? Color.Black : RetroStyle.Magenta) : (selected ? Color.FromArgb(0, 90, 90) : RetroStyle.DarkGray);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        int tagWidth = Math.Min(TextRenderer.MeasureText(e.Graphics, tag, tagFont).Width, bounds.Width / 3);
        var tagArea = new Rectangle(bounds.Right - tagWidth - S(6), bounds.Y, tagWidth, bounds.Height);
        TextRenderer.DrawText(e.Graphics, tag, tagFont, tagArea, tagColor, flags | TextFormatFlags.Right);

        int left = bounds.X + S(6) + iconSize + S(8);
        var titleArea = new Rectangle(left, bounds.Y, tagArea.Left - left - S(8), bounds.Height);
        TextRenderer.DrawText(e.Graphics, window.Title, RetroStyle.TextFont, titleArea, selected ? Color.Black : RetroStyle.White, flags);
    }

    // --- Raccourcis ---

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _registered.Clear();
        _refused.Clear();
        UpdateHotkeys();
    }

    /// <summary>Réserve ou libère les raccourcis selon l'état (filtre posé ou non, option « en plus »).</summary>
    void UpdateHotkeys()
    {
        if (!IsHandleCreated) return;
        foreach (var hotkey in Hotkeys)
        {
            bool wanted = hotkey.Id == Hotkey.Toggle || (_overlay != null && (!hotkey.Extra || _settings.ExtraHotkeys));
            if (wanted && !_registered.Contains(hotkey.Id))
            {
                if (RegisterHotKey(Handle, (int)hotkey.Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)hotkey.Key))
                {
                    _registered.Add(hotkey.Id);
                    _refused.Remove(hotkey.Id);
                }
                else
                {
                    _refused.Add(hotkey.Id);
                }
            }
            else if (!wanted)
            {
                if (_registered.Remove(hotkey.Id)) UnregisterHotKey(Handle, (int)hotkey.Id);
                _refused.Remove(hotkey.Id);
            }
        }

        bool toggle = _registered.Contains(Hotkey.Toggle);
        _shortcuts.ForeColor = toggle ? RetroStyle.White : RetroStyle.Magenta;
        _shortcuts.Text = toggle
            ? L.T("Raccourci : Ctrl+Alt+R pose ou retire le filtre sur la fenêtre active.\nPendant le filtre, Ctrl+Alt+S enregistre l'image.", "Shortcut: Ctrl+Alt+R applies or removes the filter on the active window.\nWhile the filter is on, Ctrl+Alt+S saves the image.")
            : L.T("Ctrl+Alt+R est déjà pris par une autre application : utilise le bouton ci-dessus.", "Ctrl+Alt+R is already taken by another application: use the button above.");
        if (_overlay != null) _overlay.Hint = OverlayHint;
    }

    /// <summary>Rappel des raccourcis actifs, pour la barre d'informations.</summary>
    string OverlayHint
    {
        get
        {
            string hint = "Ctrl+Alt + " + string.Join(", ", Hotkeys.Where(h => _registered.Contains(h.Id)).Select(h => h.Hint));
            if (_refused.Count > 0)
                hint += L.T("   (déjà pris : ", "   (already taken: ") + string.Join(", ", Hotkeys.Where(h => _refused.Contains(h.Id)).Select(h => h.Key)) + ")";
            return hint;
        }
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
                else Attach(WindowList.Foreground, activate: false);
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

    // --- Actions ---

    /// <summary>Message dans la fenêtre ; un problème est aussi signalé par un son et à l'écran.</summary>
    void Notify(string text, bool problem = false)
    {
        _status.Text = text;
        _status.ForeColor = problem ? RetroStyle.Magenta : RetroStyle.Gray;
        if (!problem) return;

        SystemSounds.Beep.Play();
        // Si notre fenêtre n'est pas sous les yeux de l'utilisateur, le message s'affiche à l'écran.
        if (ActiveForm == this) return;
        _toast.SetText(text, "");
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        _toast.Place(new Point(area.X + (area.Width - _toast.Width) / 2, area.Bottom - _toast.Height - S(80)), true);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Change la carte, le mode ou la palette, l'applique au filtre et le mémorise.</summary>
    void ChangeState(Action change)
    {
        change();
        _state.Save(_settings);
        _settings.Save();
        Notify(_state.Description);
        RefreshChoices();
        if (_overlay == null) return;
        _overlay.ApplyState();
        _overlay.ShowMessage(_state.Description);
    }

    void SetInfoBar(bool visible)
    {
        _settings.InfoBar = visible;
        _settings.Save();
        _infoBar.Checked = visible;
        if (_overlay != null) _overlay.InfoVisible = visible;
    }

    void SaveSnapshot()
    {
        if (_overlay == null) return;
        try
        {
            string? path = _overlay.SaveSnapshot();
            Notify(path == null ? L.T("Aucune image capturée pour l'instant.", "No image captured yet.") : L.T($"Image enregistrée : {path}", $"Image saved: {path}"));
            _overlay.ShowMessage(path == null ? L.T("Aucune image capturée pour l'instant", "No image captured yet") : L.T($"Image enregistrée : {Path.GetFileName(path)}", $"Image saved: {Path.GetFileName(path)}"));
            if (path != null) SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            Notify(L.T($"Échec de l'enregistrement : {ex.Message}", $"Could not save: {ex.Message}"), problem: true);
        }
    }

    /// <summary>Le gros bouton : pose le filtre sur la fenêtre choisie dans la liste, ou le retire.</summary>
    void ApplyClicked()
    {
        if (_overlay != null)
        {
            _overlay.Close();
            return;
        }
        if (_list.SelectedItem is not WindowInfo window)
        {
            Notify(L.T("Choisis d'abord une fenêtre dans la liste.", "Pick a window in the list first."), problem: true);
            return;
        }
        Attach(window.Handle, activate: true);
    }

    /// <summary>Pose l'overlay sur la fenêtre donnée, ou explique pourquoi ce n'est pas possible.</summary>
    void Attach(IntPtr hwnd, bool activate)
    {
        if (_selectingZone) return;
        var target = WindowList.GetCapturableWindows().FirstOrDefault(w => w.Handle == hwnd);
        if (target == null)
        {
            if (hwnd != IntPtr.Zero && WindowList.IsOwn(hwnd))
                Notify(L.T("Clique d'abord dans la fenêtre à filtrer, puis appuie sur Ctrl+Alt+R. Ou choisis-la dans la liste.", "Click in the window to filter first, then press Ctrl+Alt+R. Or pick it in the list."), problem: true);
            else if (activate)
                Notify(L.T("Cette fenêtre n'existe plus. Choisis-en une autre dans la liste.", "That window is gone. Pick another one in the list."), problem: true);
            else
                Notify(L.T("Cette fenêtre ne peut pas être filtrée (bureau, barre des tâches, fenêtre système...).", "This window cannot be filtered (desktop, taskbar, system window...)."), problem: true);
            RefreshList();
            return;
        }

        bool exclusive = WindowList.ExclusiveFullscreenRunning;

        // « Seulement une zone » : la fenêtre passe devant et on y trace le rectangle à filtrer.
        Rectangle? zone = null;
        if (_settings.ZoneOnly)
        {
            WindowList.Activate(target.Handle);
            _selectingZone = true;
            try
            {
                using var select = new ZoneSelectForm(WindowList.GetClientScreenRect(target.Handle));
                if (select.ShowDialog() != DialogResult.OK)
                {
                    Notify(L.T("Zone annulée : le filtre n'est pas posé.", "Area canceled: the filter was not applied."));
                    return;
                }
                zone = select.Selection;
            }
            finally
            {
                _selectingZone = false;
            }
            activate = true; // l'écran de sélection a pris le premier plan : on le rend à la fenêtre
        }

        try
        {
            _overlay = new OverlayForm(target.Handle, _state, zone) { InfoVisible = _settings.InfoBar };
            _overlay.FormClosed += (_, _) =>
            {
                _overlay = null;
                UpdateFilterState();
                Notify(L.T("Filtre retiré.", "Filter removed."));
            };
            _overlay.Show();
            if (activate) WindowList.Activate(target.Handle);
            UpdateFilterState();

            string removal = _registered.Contains(Hotkey.Toggle) ? L.T(" Ctrl+Alt+R pour retirer le filtre", " Ctrl+Alt+R to remove the filter") : "";
            _overlay.ShowMessage($"{_state.Description}.{removal}", 4);
            if (exclusive)
                Notify(L.T("Ce jeu semble en plein écran exclusif : si le filtre n'apparaît pas, choisis « fenêtré sans bordure » dans ses options d'affichage.", "This game seems to be in exclusive fullscreen: if the filter does not show, pick \"borderless windowed\" in its display options."), problem: true);
            else
                Notify(L.T($"Filtre posé sur : {target.Title}", $"Filter applied to: {target.Title}"));
        }
        catch (Exception ex)
        {
            _overlay = null;
            UpdateFilterState();
            Notify(L.T($"Impossible de filtrer cette fenêtre : {ex.Message}", $"Cannot filter this window: {ex.Message}"), problem: true);
        }
    }

    /// <summary>Met à jour ce qui dépend de la présence du filtre : bouton, liste, raccourcis.</summary>
    void UpdateFilterState()
    {
        bool active = _overlay != null;
        _apply.Text = active ? L.T("RETIRER LE FILTRE", "REMOVE THE FILTER") : L.T("POSER LE FILTRE", "APPLY THE FILTER");
        _apply.BackColor = active ? RetroStyle.Black : RetroStyle.Magenta;
        _apply.ForeColor = active ? RetroStyle.Magenta : Color.Black;
        _apply.FlatAppearance.BorderColor = RetroStyle.Magenta;
        _apply.FlatAppearance.MouseOverBackColor = active ? RetroStyle.DarkGray : RetroStyle.White;
        _apply.FlatAppearance.MouseDownBackColor = RetroStyle.Cyan;
        _list.Invalidate();
        UpdateHotkeys();
    }

    // --- Zone de notification ---

    /// <summary>Menu reconstruit à chaque ouverture, pour refléter le choix courant.</summary>
    void BuildTrayMenu()
    {
        _trayMenu.Items.Clear();

        string status = _overlay != null ? L.T("Filtre actif", "Filter on") : L.T("Filtre inactif : Ctrl+Alt+R dans la fenêtre à filtrer", "Filter off: Ctrl+Alt+R in the window to filter");
        _trayMenu.Items.Add(new ToolStripMenuItem(status) { Enabled = false });
        _trayMenu.Items.Add(new ToolStripSeparator());

        var machines = new ToolStripMenuItem(L.T($"Carte : {_state.Machine.Name}", $"Card: {_state.Machine.Name}"));
        for (int i = 0; i < FilterModes.Machines.Length; i++)
        {
            int index = i;
            var machine = FilterModes.Machines[i];
            machines.DropDownItems.Add(new ToolStripMenuItem($"{machine.Name}, {machine.Detail}", null,
                (_, _) => ChangeState(() => _state.SetMachine(index))) { Checked = i == _state.MachineIndex });
        }
        _trayMenu.Items.Add(machines);

        // Mode : moderne et historique en premier ; les tailles de pixel intermédiaires dans « Avancé ».
        var modes = new ToolStripMenuItem(L.T($"Mode : {_state.Mode.Name}", $"Mode: {_state.Mode.Name}"));
        var advancedModes = new ToolStripMenuItem(L.T("Avancé", "Advanced"));
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

        var palettes = new ToolStripMenuItem(L.T($"Palette : {_state.Palette.Name}", $"Palette: {_state.Palette.Name}"));
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
        _trayMenu.Items.Add(new ToolStripMenuItem(L.T("Retirer le filtre", "Remove the filter"), null, (_, _) => _overlay?.Close()) { Enabled = _overlay != null });
        _trayMenu.Items.Add(new ToolStripMenuItem(L.T("Ouvrir retro-filter", "Open retro-filter"), null, (_, _) => ShowMainWindow()));
        _trayMenu.Items.Add(new ToolStripMenuItem(L.T("Ouvrir le dossier des images", "Open the image folder"), null, (_, _) => OpenSnapshotFolder()));

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem(L.T("Quitter", "Quit"), null, (_, _) => Exit()));
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

    /// <summary>Pour les tests : ferme le menu de la zone de notification et déplie ou replie « Avancé ».</summary>
    internal void PrepareForTest(bool advanced, bool demo = false)
    {
        _trayMenu.Close();
        SetAdvancedVisible(advanced);
        RefreshList();
        if (!demo) return;

        // Liste fictive : les captures publiées ne doivent pas montrer les fenêtres de l'utilisateur.
        _demoList = true;
        _settings.ZoneOnly = false; // affichage seulement : rien n'est enregistré
        RefreshChoices();
        _list.Items.Clear();
        (string Title, string Process)[] windows =
        [
            (L.T("Mon jeu préféré", "My favorite game"), L.T("jeu.exe", "game.exe")),
            (L.T("Une vidéo en ligne - Navigateur", "An online video - Browser"), L.T("navigateur.exe", "browser.exe")),
            (L.T("Film de vacances - Lecteur vidéo", "Holiday film - Video player"), L.T("lecteur.exe", "player.exe")),
            (L.T("Photos de famille", "Family photos"), "photos.exe"),
            (L.T("Bloc-notes", "Notepad"), "notes.exe"),
        ];
        for (int i = 0; i < windows.Length; i++)
            _list.Items.Add(new WindowInfo(new IntPtr(-100 - i), windows[i].Title, windows[i].Process));
        _list.SelectedIndex = 0;
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
        // La croix cache la fenêtre : l'app continue (raccourci, filtre) dans la zone de notification.
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _tray.ShowBalloonTip(5000, L.T("retro-filter continue en arrière-plan", "retro-filter keeps running in the background"),
                    L.T("Ctrl+Alt+R dans une fenêtre pour la filtrer. Clic sur cette icône pour rouvrir retro-filter, clic droit pour quitter.", "Ctrl+Alt+R in a window to filter it. Click this icon to reopen retro-filter, right-click to quit."),
                    ToolTipIcon.None);
                _settings.TrayHintShown = true;
                _settings.Save();
            }
            return;
        }
        base.OnFormClosing(e);
    }

    // --- Aperçu de test (Avancé) ---

    void OpenPreview()
    {
        if (_list.SelectedItem is not WindowInfo window || !WindowList.IsAlive(window.Handle))
        {
            Notify(L.T("Choisis d'abord une fenêtre dans la liste.", "Pick a window in the list first."), problem: true);
            RefreshList();
            return;
        }

        try
        {
            new PreviewForm(window).Show(this);
        }
        catch (Exception ex)
        {
            Notify(L.T($"Capture impossible : {ex.Message}", $"Cannot capture: {ex.Message}"), problem: true);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _overlay?.Close();
        foreach (var id in _registered) UnregisterHotKey(Handle, (int)id);
        _registered.Clear();
        _refresh.Dispose();
        _toastTimer.Dispose();
        _toast.Dispose();
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
