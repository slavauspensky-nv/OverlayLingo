using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WordPeek;

internal sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CheckBox _enabled;
    private readonly CheckBox _autostart;
    private readonly CheckBox _browserAccessibility;
    private readonly CheckBox _keyboardFallback;
    private readonly ComboBox _screenshotSource;
    private readonly ComboBox _fontFamily;
    private readonly ComboBox _fontSize;
    private readonly ColorPicker _fontColor;
    private readonly ColorPicker _bgColor;
    private readonly Border _previewFrame;
    private readonly TextBlock _previewText;
    private readonly Slider _opacity;
    private readonly TextBlock _opacityText;
    private readonly Slider _speed;
    private readonly TextBlock _speedText;
    private readonly CheckBox _sound;
    private readonly ComboBox _soundVariant;
    private readonly CheckBox _typing;
    private readonly Slider _volume;
    private readonly TextBlock _volumeText;
    private readonly Button _hotkeyButton;
    private readonly ComboBox _provider;
    private readonly TextBox _deeplKey;
    private readonly TextBox _debounce;
    private readonly TextBlock _status;

    private string? _capturingFor;
    private uint _modifiers;
    private uint _key;
    private uint _reopenModifiers;
    private uint _reopenKey;
    private readonly Button _reopenHotkeyButton;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
                _modifiers = settings.HotkeyModifiers;
        _key = settings.HotkeyKey;
        _reopenModifiers = settings.ReopenHotkeyModifiers;
        _reopenKey = settings.ReopenHotkeyKey;

        Title = "WordPeek — настройки";
        Width = 470;
        SizeToContent = SizeToContent.Manual;
        Height = Math.Max(420, Math.Min(1000, SystemParameters.WorkArea.Height * 0.9));
        MinWidth = 400;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));

        var panel = new StackPanel { Margin = new Thickness(20) };

        _enabled = new CheckBox
        {
            Content = "Перевод включён",
            IsChecked = settings.Enabled,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(_enabled);

        _autostart = new CheckBox
        {
            Content = "Запускать при входе в Windows",
            IsChecked = Autostart.IsEnabled(),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(_autostart);

        _browserAccessibility = new CheckBox
        {
            Content = "Доступ к полям ввода в браузерах и Telegram",
            IsChecked = settings.BrowserAccessibility,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(_browserAccessibility);
        panel.Children.Add(Hint(
            "Без этого Chrome, Telegram и подобные приложения скрывают текст поля. " +
            "При выходе настройка возвращается как была."));

        _keyboardFallback = new CheckBox
        {
            Content = "Перехват клавиатуры (запасной способ)",
            IsChecked = settings.KeyboardFallback,
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 4)
        };
        panel.Children.Add(_keyboardFallback);
        panel.Children.Add(Hint(
            "Нужен там, где доступность не помогла. Помните: в этом режиме программа " +
            "видит всё, что вы печатаете, включая пароли. Выключайте, если не нужно."));

        panel.Children.Add(Caption("Горячая клавиша включения/выключения"));
        _hotkeyButton = new Button
        {
            Content = HotkeyText(_modifiers, _key),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            MinWidth = 170
        };
        _hotkeyButton.Click += (_, _) => StartCapture("toggle");
        panel.Children.Add(_hotkeyButton);
        panel.Children.Add(Hint("Нажмите кнопку и введите новую комбинацию."));

        panel.Children.Add(Caption("Вызов последнего окна перевода"));
        _reopenHotkeyButton = new Button
        {
            Content = HotkeyText(_reopenModifiers, _reopenKey),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            MinWidth = 170
        };
        _reopenHotkeyButton.Click += (_, _) => StartCapture("reopen");
        panel.Children.Add(_reopenHotkeyButton);
        panel.Children.Add(Hint("Повторно показывает последний перевод после закрытия."));

        panel.Children.Add(Caption("Сервис перевода"));
        _provider = new ComboBox { FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
        _provider.Items.Add(new ComboBoxItem { Content = "Google (бесплатно, без ключа)", Tag = "google" });
        _provider.Items.Add(new ComboBoxItem { Content = "DeepL (нужен ключ API)", Tag = "deepl" });
        _provider.SelectedIndex = settings.Provider == "deepl" ? 1 : 0;
        panel.Children.Add(_provider);

        _deeplKey = new TextBox
        {
            Text = settings.DeeplKey,
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 6, 0, 0)
        };
        panel.Children.Add(_deeplKey);
        panel.Children.Add(Hint("Ключ DeepL нужен только если выбран DeepL."));

        var checkButton = new Button
        {
            Content = "Проверить связь",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        checkButton.Click += (_, _) => CheckConnection();
        panel.Children.Add(checkButton);

        panel.Children.Add(Caption("Язык текста на скриншоте"));
        _screenshotSource = new ComboBox { FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
        _screenshotSource.Items.Add(new ComboBoxItem { Content = "Определять автоматически", Tag = "auto" });
        _screenshotSource.Items.Add(new ComboBoxItem { Content = "Английский → русский", Tag = "en" });
        _screenshotSource.Items.Add(new ComboBoxItem { Content = "Русский → английский", Tag = "ru" });

        int selectedIndex = settings.ScreenshotSource switch
        {
            "en" => 1,
            "ru" => 2,
            _ => 0
        };
        _screenshotSource.SelectedIndex = selectedIndex;
        panel.Children.Add(_screenshotSource);
        panel.Children.Add(Hint("Если язык определён неверно — выберите его вручную."));

        panel.Children.Add(Caption("Оформление окна перевода"));

        _fontFamily = new ComboBox { FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };
        foreach (string family in FontHelper.Names)
        {
            _fontFamily.Items.Add(family);
        }
        if (!_fontFamily.Items.Contains(settings.BubbleFont))
        {
            _fontFamily.Items.Add(settings.BubbleFont);
        }
        _fontFamily.SelectedItem = settings.BubbleFont;
        _fontFamily.SelectionChanged += (_, _) => RefreshPreview();
        panel.Children.Add(_fontFamily);

        _fontSize = new ComboBox
        {
            FontSize = 13,
            IsEditable = true,
            Text = settings.BubbleFontSize.ToString("0.#"),
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };
        foreach (string size in new[] { "11", "12", "14", "16", "18", "20", "24", "28" })
        {
            _fontSize.Items.Add(size);
        }
        _fontSize.SelectionChanged += (_, _) => RefreshPreview();
        _fontSize.LostFocus += (_, _) => RefreshPreview();
        _fontSize.KeyUp += (_, _) => RefreshPreview();
        panel.Children.Add(_fontSize);

        _fontColor = new ColorPicker(ParseColor(settings.BubbleFontColor, Colors.White));
        _fontColor.Changed += _ => RefreshPreview();
        panel.Children.Add(LabeledRow("Цвет текста", _fontColor));

        _bgColor = new ColorPicker(ParseColor(settings.BubbleBackground, Color.FromRgb(0x1B, 0x1D, 0x21)));
        _bgColor.Changed += _ => RefreshPreview();
        panel.Children.Add(LabeledRow("Цвет фона", _bgColor));

        panel.Children.Add(Hint("Нажмите на образец цвета — откроется палитра."));

        panel.Children.Add(Caption("Предпросмотр"));

        _previewText = new TextBlock
        {
            Text = "Пример: Hello, как дела?",
            TextWrapping = TextWrapping.Wrap
        };

        _previewFrame = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 11),
            Child = _previewText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 6)
        };

        panel.Children.Add(_previewFrame);

        panel.Children.Add(Caption("Прозрачность фона"));

        _opacity = new Slider
        {
            Minimum = 0.2,
            Maximum = 1.0,
            Value = Math.Clamp(settings.BubbleOpacity, 0.2, 1.0),
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true
        };
        panel.Children.Add(_opacity);

        _opacityText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)),
            Text = $"{_opacity.Value * 100:0}%"
        };
        _opacity.ValueChanged += (_, _) =>
        {
            _opacityText.Text = $"{_opacity.Value * 100:0}%";
            RefreshPreview();
        };
        panel.Children.Add(_opacityText);

        panel.Children.Add(Caption("Скорость печати текста"));

        _typing = new CheckBox
        {
            Content = "Печатать текст по буквам (эффект машинки)",
            IsChecked = settings.BubbleTyping,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(_typing);

        _speed = new Slider
        {
            Minimum = 0,
            Maximum = 200,
            Value = Math.Clamp(settings.BubbleTypeSpeed, 0, 200),
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            TickFrequency = 5,
            IsSnapToTickEnabled = true
        };
        panel.Children.Add(_speed);

        _speedText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)),
            Text = SpeedText(_speed.Value)
        };
        _speed.ValueChanged += (_, _) => _speedText.Text = SpeedText(_speed.Value);
        panel.Children.Add(_speedText);
        panel.Children.Add(Hint("0 — текст появляется сразу, без анимации."));

        panel.Children.Add(Caption("Звук печати"));

        _sound = new CheckBox
        {
            Content = "Озвучивать набор текста",
            IsChecked = settings.BubbleSound,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(_sound);

        _soundVariant = new ComboBox { FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };

        foreach (var variant in TypingSound.Variants)
        {
            _soundVariant.Items.Add(new ComboBoxItem { Content = variant.Title, Tag = variant.Id });
        }

        int soundIndex = 0;
        for (int i = 0; i < TypingSound.Variants.Length; i++)
        {
            if (string.Equals(TypingSound.Variants[i].Id, settings.BubbleSoundVariant, StringComparison.OrdinalIgnoreCase))
            {
                soundIndex = i;
                break;
            }
        }

        _soundVariant.SelectedIndex = soundIndex;
        panel.Children.Add(_soundVariant);

        var playSound = new Button
        {
            Content = "Прослушать",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        };
        playSound.Click += (_, _) => _ = PlaySoundDemo();
        panel.Children.Add(playSound);

        _volume = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = Math.Clamp(settings.BubbleSoundVolume, 0, 1),
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true
        };
        panel.Children.Add(_volume);

        _volumeText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)),
            Text = $"Громкость {_volume.Value * 100:0}%"
        };
        _volume.ValueChanged += (_, _) => _volumeText.Text = $"Громкость {_volume.Value * 100:0}%";
        panel.Children.Add(_volumeText);

        // Без анимации печати скорость и звук смысла не имеют.
        _typing.Checked += (_, _) => ApplyTypingState();
        _typing.Unchecked += (_, _) => ApplyTypingState();
        ApplyTypingState();

        RefreshPreview();

        var resetPosition = new Button
        {
            Content = "Вернуть позицию окна по умолчанию",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 5, 14, 5),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        };
        resetPosition.Click += (_, _) =>
        {
            _settings.BubbleLeft = -1;
            _settings.BubbleTop = -1;
            _settings.Save();

            if (_status is not null)
            {
                _status.Text = "Позиция окна сброшена: снова встаёт рядом с выделением.";
            }
        };
        panel.Children.Add(resetPosition);

        panel.Children.Add(Caption("Задержка перед отправкой, мс"));
        _debounce = new TextBox
        {
            Text = settings.DebounceMs.ToString(),
            FontSize = 13,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4, 6, 4)
        };
        panel.Children.Add(_debounce);
        panel.Children.Add(Hint("120–150 мс ощущается как мгновенно и экономит лимит запросов."));

        _status = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x00)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };
        panel.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var save = new Button
        {
            Content = "Сохранить",
            Padding = new Thickness(20, 7, 20, 7),
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0)
        };
        save.Click += (_, _) => Save();

        var cancel = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(20, 7, 20, 7),
            FontSize = 13
        };
        cancel.Click += (_, _) => Close();

        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        buttons.Margin = new Thickness(20, 10, 20, 14);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
            Padding = new Thickness(0, 0, 6, 0)
        };

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(scroller);

        Content = root;
    }

    public event Action? Saved;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingFor is null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        if (key == Key.Escape)
        {
            FinishCapture();
            return;
        }

        uint mods = 0;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            mods |= Native.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            mods |= Native.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            mods |= Native.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            mods |= Native.MOD_WIN;
        }

        if (mods == 0)
        {
                        ActiveHotkeyButton().Content = "Нужен хотя бы один модификатор";
            return;
        }

                if (_capturingFor == "reopen")
        {
            _reopenModifiers = mods;
            _reopenKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        }
        else
        {
            _modifiers = mods;
            _key = (uint)KeyInterop.VirtualKeyFromKey(key);
        }

        FinishCapture();
    }

        private void StartCapture(string target)
    {
        _capturingFor = target;
        ActiveHotkeyButton().Content = "Нажмите комбинацию…";
        ActiveHotkeyButton().Focus();
    }

    private Button ActiveHotkeyButton()
    {
        return _capturingFor == "reopen" ? _reopenHotkeyButton : _hotkeyButton;
    }

    private void FinishCapture()
    {
        _hotkeyButton.Content = HotkeyText(_modifiers, _key);

        if (_reopenHotkeyButton is not null)
        {
            _reopenHotkeyButton.Content = HotkeyText(_reopenModifiers, _reopenKey);
        }

        _capturingFor = null;
    }

    private void Save()
    {
        _settings.Enabled = _enabled.IsChecked == true;
        _settings.StartWithWindows = _autostart.IsChecked == true;
        _settings.BrowserAccessibility = _browserAccessibility.IsChecked == true;
        _settings.KeyboardFallback = _keyboardFallback.IsChecked == true;
        _settings.ScreenshotSource = (_screenshotSource.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
                _settings.HotkeyModifiers = _modifiers;
        _settings.HotkeyKey = _key;
        _settings.ReopenHotkeyModifiers = _reopenModifiers;
        _settings.ReopenHotkeyKey = _reopenKey;
        _settings.Provider = (_provider.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "google";
        _settings.DeeplKey = _deeplKey.Text.Trim();

        if (_fontFamily.SelectedItem is string family && !string.IsNullOrWhiteSpace(family))
        {
            _settings.BubbleFont = family;
        }

        if (double.TryParse(_fontSize.Text.Trim(), out double fontSize))
        {
            _settings.BubbleFontSize = Math.Clamp(fontSize, 9, 48);
        }

        _settings.BubbleFontColor = ColorPicker.Hex(_fontColor.Selected);
        _settings.BubbleBackground = ColorPicker.Hex(_bgColor.Selected);
        _settings.BubbleOpacity = Math.Clamp(_opacity.Value, 0.2, 1.0);
        _settings.BubbleTypeSpeed = (int)Math.Round(_speed.Value);
        _settings.BubbleTyping = _typing.IsChecked == true;
        _settings.BubbleSound = _sound.IsChecked == true;
        _settings.BubbleSoundVariant = (_soundVariant.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? TypingSound.DefaultVariant;
        _settings.BubbleSoundVolume = Math.Clamp(_volume.Value, 0.0, 1.0);

        if (int.TryParse(_debounce.Text.Trim(), out int debounce))
        {
            _settings.DebounceMs = Math.Clamp(debounce, 60, 2000);
        }

        Autostart.Apply(_settings.StartWithWindows);

        _settings.Save();
        Saved?.Invoke();
        Close();
    }

    private async void CheckConnection()
    {
        _status.Text = "Проверяю связь…";

        var probe = new AppSettings
        {
            Provider = (_provider.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "google",
            DeeplKey = _deeplKey.Text.Trim()
        };

        try
        {
            var translator = new Translator(probe);
            string? result = await translator.TranslateAsync("Hello, world!", CancellationToken.None);
            _status.Text = string.IsNullOrEmpty(result)
                ? "Не удалось перевести. Проверьте интернет или ключ DeepL."
                : "Работает: Hello, world! → " + result;
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка: " + ex.Message;
        }
    }

    private static string HotkeyText(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        return KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString();
    }

    /// <summary>Живой предпросмотр: шрифт, размер, цвета и прозрачность как в окне перевода.</summary>
    private void RefreshPreview()
    {
        if (_previewText is null || _previewFrame is null || _fontColor is null || _bgColor is null)
        {
            return;
        }

        string family = _fontFamily?.SelectedItem as string ?? "Segoe UI";
        double size = double.TryParse(_fontSize?.Text?.Trim(), out double parsed) ? Math.Clamp(parsed, 9, 48) : 14.5;

        _previewText.FontFamily = FontHelper.Resolve(family);
        _previewText.FontSize = size;
        _previewText.LineHeight = size * 1.38;
        _previewText.Foreground = new SolidColorBrush(_fontColor.Selected);

        Color background = _bgColor.Selected;
        double opacity = Math.Clamp(_opacity?.Value ?? 0.95, 0.2, 1.0);
        byte alpha = (byte)Math.Round(opacity * 255);

        _previewFrame.Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
    }

    private void ApplyTypingState()
    {
        bool enabled = _typing.IsChecked == true;

        _speed.IsEnabled = enabled;
        _sound.IsEnabled = enabled;
        _soundVariant.IsEnabled = enabled;
        _volume.IsEnabled = enabled;
    }

    /// <summary>Короткая демонстрация выбранного звука набора.</summary>
    private async Task PlaySoundDemo()
    {
        string id = (_soundVariant.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? TypingSound.DefaultVariant;
        double volume = _volume?.Value ?? 0.6;

        for (int i = 0; i < 14; i++)
        {
            TypingSound.Play(id, volume);
            await Task.Delay(75);
        }
    }

    private static string SpeedText(double value)
    {
        return value <= 0 ? "без анимации" : $"{value:0} символов в секунду";
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static StackPanel LabeledRow(string label, UIElement control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(control);
        return row;
    }

    private static Border Swatch()
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC4))
        };
    }

    private static StackPanel ColorRow(string label, TextBox box, Border swatch)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(box);
        row.Children.Add(swatch);
        return row;
    }

    private static void UpdateSwatch(TextBox box, Border swatch)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(box.Text.Trim());
            swatch.Background = new SolidColorBrush(color);
        }
        catch
        {
            swatch.Background = Brushes.Transparent;
        }
    }

    private static string NormalizeColor(string input, string fallback)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(input.Trim());
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return fallback;
        }
    }

    private static TextBlock Caption(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3C, 0x40, 0x48)),
            Margin = new Thickness(0, 14, 0, 6)
        };
    }

    private static TextBlock Hint(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }
}
