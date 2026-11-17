using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WordPeek;

/// <summary>Тренировка слов в духе Anki: карточка, четыре варианта, прогресс раунда.</summary>
internal sealed class TrainingWindow : Window
{
    private const int RoundLength = 10;

    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush Card = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly Brush Ridge = new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xE0));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x28));
    private static readonly Brush Right = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush RightFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
    private static readonly Brush Wrong = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush WrongFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));

    private readonly List<NoteEntry> _all;
    private readonly ComboBox _days;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressText;
    private readonly TextBlock _prompt;
    private readonly TextBlock _hint;
    private readonly StackPanel _options;
    private readonly TextBlock _feedback;
    private readonly Button _next;
    private readonly Border _card;
    private readonly StackPanel _quizArea;
    private readonly StackPanel _summaryArea;
    private readonly TextBlock _summaryTitle;
    private readonly TextBlock _summaryText;

    private readonly List<Button> _buttons = new();

    private List<QuizQuestion> _session = new();
    private int _index;
    private int _correct;
    private bool _answered;

    public TrainingWindow(string? notesPath = null)
    {
        Title = "Обучение — WordPeek";
        Width = 580;
        Height = 620;
        MinWidth = 480;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));

        _all = TrainingData.Load(notesPath);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- шапка: день и раунд ---
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dayBlock = new StackPanel { Orientation = Orientation.Horizontal };
        dayBlock.Children.Add(new TextBlock
        {
            Text = "День:",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        _days = new ComboBox { FontSize = 13, MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        _days.SelectionChanged += (_, _) => StartRound();
        dayBlock.Children.Add(_days);
        header.Children.Add(dayBlock);

        _progressText = new TextBlock
        {
            FontSize = 12,
            Foreground = Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_progressText, 1);
        header.Children.Add(_progressText);

        var restart = new Button
        {
            Content = "Новый раунд",
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12
        };
        restart.Click += (_, _) => StartRound();
        Grid.SetColumn(restart, 2);
        header.Children.Add(restart);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _progress = new ProgressBar
        {
            Height = 5,
            Minimum = 0,
            Maximum = RoundLength,
            Value = 0,
            Foreground = new SolidColorBrush(Color.FromRgb(0x39, 0x8B, 0xE0)),
            Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEE)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(_progress, 1);
        root.Children.Add(_progress);

        // --- рабочая область ---
        var body = new Grid();
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        _quizArea = new StackPanel();
        _summaryArea = new StackPanel { Visibility = Visibility.Collapsed };

        body.Children.Add(_quizArea);
        body.Children.Add(_summaryArea);

        _card = new Border
        {
            Background = Card,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 26, 20, 26),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var cardStack = new StackPanel();

        _prompt = new TextBlock
        {
            Text = "",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ink
        };

        _hint = new TextBlock
        {
            Text = "",
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Foreground = Muted,
            Margin = new Thickness(0, 8, 0, 0)
        };

        cardStack.Children.Add(_prompt);
        cardStack.Children.Add(_hint);
        _card.Child = cardStack;
        _quizArea.Children.Add(_card);

        _options = new StackPanel();
        _quizArea.Children.Add(_options);

        _feedback = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _quizArea.Children.Add(_feedback);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        _next = new Button
        {
            Content = "Далее",
            Padding = new Thickness(22, 8, 22, 8),
            FontSize = 13,
            IsEnabled = false,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _next.Click += (_, _) => Advance();

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 13
        };
        close.Click += (_, _) => Close();

        bottom.Children.Add(_next);
        bottom.Children.Add(close);
        _quizArea.Children.Add(bottom);

        // --- итоги раунда ---
        var summaryCard = new Border
        {
            Background = Paper,
            BorderBrush = Ridge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24, 30, 24, 30)
        };

        var summaryStack = new StackPanel();

        _summaryTitle = new TextBlock
        {
            Text = "",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = Ink
        };

        _summaryText = new TextBlock
        {
            Text = "",
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Foreground = Muted,
            Margin = new Thickness(0, 10, 0, 20),
            TextWrapping = TextWrapping.Wrap
        };

        var again = new Button
        {
            Content = "Ещё раунд",
            Padding = new Thickness(24, 9, 24, 9),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        again.Click += (_, _) => StartRound();

        summaryStack.Children.Add(_summaryTitle);
        summaryStack.Children.Add(_summaryText);
        summaryStack.Children.Add(again);
        summaryCard.Child = summaryStack;
        _summaryArea.Children.Add(summaryCard);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };

        PreviewKeyDown += OnKey;

        FillDays();

        if (_all.Count == 0)
        {
            ShowEmpty();
        }
        else
        {
            StartRound();
        }
    }

    private void FillDays()
    {
        _days.Items.Add(new ComboBoxItem { Content = "Все дни", Tag = "" });

        foreach (string day in _all.Select(e => e.Day).Where(d => d.Length > 0).Distinct())
        {
            _days.Items.Add(new ComboBoxItem { Content = day, Tag = day });
        }

        _days.SelectedIndex = 0;
    }

    private List<NoteEntry> CurrentEntries()
    {
        string day = (_days.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        return day.Length == 0
            ? _all.ToList()
            : _all.Where(e => string.Equals(e.Day, day, StringComparison.Ordinal)).ToList();
    }

    private void StartRound()
    {
        var entries = CurrentEntries();

        if (entries.Count == 0)
        {
            ShowEmpty();
            return;
        }

        _session = new List<QuizQuestion>();
        string? previous = null;

        int count = Math.Min(RoundLength, Math.Max(1, entries.Select(e => e.Word).Distinct().Count()));

        for (int i = 0; i < count; i++)
        {
            QuizQuestion? question = QuizBuilder.Build(entries, previous);

            if (question is null)
            {
                break;
            }

            previous = question.Prompt;
            _session.Add(question);
        }

        if (_session.Count == 0)
        {
            ShowEmpty();
            return;
        }

        _index = 0;
        _correct = 0;

        _quizArea.Visibility = Visibility.Visible;
        _summaryArea.Visibility = Visibility.Collapsed;

        _progress.Maximum = _session.Count;
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        _answered = false;

        if (_index >= _session.Count)
        {
            ShowSummary();
            return;
        }

        QuizQuestion question = _session[_index];

        _prompt.Text = question.Prompt;
        _hint.Text = QuizBuilder.HasCyrillic(question.Prompt) ? "выберите перевод на английский" : "выберите перевод на русский";
        _feedback.Text = "";
        _next.IsEnabled = false;

        _progress.Value = _index;
        _progressText.Text = $"Вопрос {_index + 1} из {_session.Count} · верно {_correct}";

        _options.Children.Clear();
        _buttons.Clear();

        for (int i = 0; i < question.Options.Count; i++)
        {
            string option = question.Options[i];

            var button = new Button
            {
                Content = $"{i + 1}.  {option}",
                FontSize = 15,
                Padding = new Thickness(16, 11, 16, 11),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Paper,
                BorderThickness = new Thickness(1),
                BorderBrush = Ridge,
                Cursor = Cursors.Hand
            };

            button.Click += (_, _) => Answer(button, option);
            _options.Children.Add(button);
            _buttons.Add(button);
        }
    }

    private void Answer(Button button, string option)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        QuizQuestion question = _session[_index];
        bool right = string.Equals(option, question.Answer, StringComparison.Ordinal);

        if (right)
        {
            _correct++;
            Paint(button, RightFill, Right);
            _feedback.Foreground = Right;
            _feedback.Text = $"Верно · {question.Answer}";
        }
        else
        {
            Paint(button, WrongFill, Wrong);
            _feedback.Foreground = Wrong;
            _feedback.Text = $"Правильный ответ: {question.Answer}";

            foreach (var other in _buttons)
            {
                if (other.Content is string text && text.EndsWith(question.Answer, StringComparison.Ordinal))
                {
                    Paint(other, RightFill, Right);
                }
            }
        }

        foreach (var other in _buttons)
        {
            other.IsEnabled = false;
        }

        _progress.Value = _index + 1;
        _progressText.Text = $"Вопрос {_index + 1} из {_session.Count} · верно {_correct}";
        _next.IsEnabled = true;
        _next.Focus();
    }

    private static void Paint(Button button, Brush fill, Brush border)
    {
        button.Background = fill;
        button.BorderBrush = border;
        button.IsEnabled = true;
    }

    private void Advance()
    {
        if (!_answered)
        {
            return;
        }

        _index++;
        ShowQuestion();
    }

    private void ShowSummary()
    {
        _quizArea.Visibility = Visibility.Collapsed;
        _summaryArea.Visibility = Visibility.Visible;

        _progress.Value = _session.Count;

        int total = _session.Count;
        double share = total == 0 ? 0 : (double)_correct / total;

        _summaryTitle.Text = share switch
        {
            >= 0.9 => "Отлично!",
            >= 0.7 => "Хорошо",
            >= 0.4 => "Есть над чем поработать",
            _ => "Стоит повторить"
        };

        _summaryText.Text = $"Верно {_correct} из {total} ({share * 100:0}%).\n" +
                            "Нажми «Ещё раунд», чтобы пройти заново — слова будут другие.";

        _progressText.Text = $"Раунд завершён · {_correct} из {total}";
    }

    private void ShowEmpty()
    {
        _quizArea.Visibility = Visibility.Visible;
        _summaryArea.Visibility = Visibility.Collapsed;

        _prompt.Text = "Список слов пуст";
        _hint.Text = TrainingData.NotesPath();
        _feedback.Text = "Добавляй слова двойным кликом по слову в окне перевода — они появятся в WordPeek.txt.";
        _feedback.Foreground = Muted;

        _options.Children.Clear();
        _next.IsEnabled = false;
        _progress.Value = 0;
        _progressText.Text = "Нет слов";
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.D1 and <= Key.D4 && !_answered)
        {
            int index = e.Key - Key.D1;

            if (index < _buttons.Count)
            {
                _buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            if (_answered)
            {
                Advance();
            }

            e.Handled = true;
        }
    }
}
