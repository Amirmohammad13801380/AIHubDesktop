using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIHubDesktop.Models;
using AIHubDesktop.Services;

namespace AIHubDesktop;

public partial class MainWindow : Window
{
    private readonly AiApiClient _apiClient = new();

    private readonly StoredSettings _settings;

    private readonly List<ChatMessage> _messages = new();

    private CancellationTokenSource? _requestCancellation;

    private readonly List<ProviderDefinition> _providers =
    [
        new(
            "ollama",
            "Ollama — محلی و بدون API",
            ApiProtocol.Ollama,
            "http://localhost:11434",
            false),

        new(
            "lmstudio",
            "LM Studio — محلی و بدون API",
            ApiProtocol.OpenAiCompatible,
            "http://localhost:1234/v1",
            false),

        new(
            "openrouter",
            "OpenRouter — مدل‌های رایگان",
            ApiProtocol.OpenAiCompatible,
            "https://openrouter.ai/api/v1",
            true,
            true,
            "https://openrouter.ai/settings/keys"),

        new(
            "gemini",
            "Google Gemini — سهمیهٔ رایگان",
            ApiProtocol.Gemini,
            "https://generativelanguage.googleapis.com/v1beta",
            true,
            false,
            "https://aistudio.google.com/apikey"),

        new(
            "groq",
            "Groq — Developer Plan",
            ApiProtocol.OpenAiCompatible,
            "https://api.groq.com/openai/v1",
            true,
            false,
            "https://console.groq.com/keys"),

        new(
            "cerebras",
            "Cerebras — اعتبار آزمایشی",
            ApiProtocol.OpenAiCompatible,
            "https://api.cerebras.ai/v1",
            true,
            false,
            "https://cloud.cerebras.ai/"),

        new(
            "huggingface",
            "Hugging Face Inference",
            ApiProtocol.OpenAiCompatible,
            "https://router.huggingface.co/v1",
            true,
            false,
            "https://huggingface.co/settings/tokens"),

        new(
            "nvidia",
            "NVIDIA NIM",
            ApiProtocol.OpenAiCompatible,
            "https://integrate.api.nvidia.com/v1",
            true,
            false,
            "https://build.nvidia.com/settings/api-keys"),

        new(
            "deepseek",
            "DeepSeek API رسمی — معمولاً پولی",
            ApiProtocol.OpenAiCompatible,
            "https://api.deepseek.com/v1",
            true,
            false,
            "https://platform.deepseek.com/api_keys"),

        new(
            "moonshot",
            "Moonshot/Kimi API رسمی — معمولاً پولی",
            ApiProtocol.OpenAiCompatible,
            "https://api.moonshot.ai/v1",
            true,
            false,
            "https://platform.moonshot.ai/console/api-keys"),

        new(
            "openai",
            "OpenAI API — پولی",
            ApiProtocol.OpenAiCompatible,
            "https://api.openai.com/v1",
            true,
            false,
            "https://platform.openai.com/api-keys"),

        new(
            "custom",
            "API سفارشی سازگار با OpenAI",
            ApiProtocol.OpenAiCompatible,
            "http://localhost:8080/v1",
            false)
    ];

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsVault.Load();

        ProviderComboBox.ItemsSource = _providers;

        ProviderDefinition? selected =
            _providers.FirstOrDefault(
                x => x.Id == _settings.LastProviderId)
            ?? _providers[0];

        ProviderComboBox.SelectedItem = selected;

        Loaded += async (_, _) =>
        {
            await TryLoadModelsAsync(showErrors: false);
        };
    }

    private ProviderDefinition? CurrentProvider =>
        ProviderComboBox.SelectedItem as ProviderDefinition;

    private void ProviderComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CurrentProvider is not { } provider)
        {
            return;
        }

        string baseUrl = provider.DefaultBaseUrl;

        if (_settings.BaseUrls.TryGetValue(
                provider.Id,
                out string? savedBaseUrl) &&
            !string.IsNullOrWhiteSpace(savedBaseUrl))
        {
            baseUrl = savedBaseUrl;
        }

        BaseUrlTextBox.Text = baseUrl;

        if (_settings.EncryptedApiKeys.TryGetValue(
                provider.Id,
                out string? encryptedKey))
        {
            ApiKeyPasswordBox.Password =
                SettingsVault.Unprotect(encryptedKey);
        }
        else
        {
            ApiKeyPasswordBox.Password = string.Empty;
        }

        ApiKeyPasswordBox.IsEnabled =
            provider.RequiresApiKey ||
            provider.Id == "custom";

        OpenApiPageButton.IsEnabled =
            !string.IsNullOrWhiteSpace(provider.ApiKeyPage);

        ApiKeyHelpText.Text = provider switch
        {
            { Id: "ollama" } =>
                "کاملاً محلی است و API Key نمی‌خواهد.",

            { Id: "lmstudio" } =>
                "سرور محلی LM Studio را فعال کنید. API Key لازم نیست.",

            { Id: "openrouter" } =>
                "فقط مدل‌هایی نمایش داده می‌شوند که قیمت ورودی و خروجی آن‌ها صفر باشد.",

            { Id: "gemini" } =>
                "مدل‌های موجود حساب دریافت می‌شوند. سهمیهٔ رایگان و محدودیت منطقه‌ای ممکن است اعمال شود.",

            { Id: "deepseek" or "moonshot" or "openai" } =>
                "این API رسمی معمولاً رایگان دائمی نیست. هزینه و موجودی حساب را بررسی کنید.",

            _ =>
                provider.RequiresApiKey
                    ? "API Key حساب خود را وارد کنید."
                    : "در صورت نیاز API Key را وارد کنید."
        };

        ModelComboBox.ItemsSource = null;
        ModelComboBox.Text = string.Empty;

        StatusTextBlock.Text =
            $"ارائه‌دهنده انتخاب شد: {provider.Name}";
    }

    private void SaveSettingsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (CurrentProvider is not { } provider)
        {
            return;
        }

        SaveCurrentProviderSettings(provider);

        StatusTextBlock.Text =
            "تنظیمات و API Key به‌صورت رمزگذاری‌شده ذخیره شد.";
    }

    private void SaveCurrentProviderSettings(
        ProviderDefinition provider)
    {
        _settings.LastProviderId = provider.Id;

        _settings.BaseUrls[provider.Id] =
            BaseUrlTextBox.Text.Trim();

        _settings.EncryptedApiKeys[provider.Id] =
            SettingsVault.Protect(
                ApiKeyPasswordBox.Password.Trim());

        _settings.LastModel =
            GetSelectedModelId();

        SettingsVault.Save(_settings);
    }

    private async void LoadModelsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await TryLoadModelsAsync(showErrors: true);
    }

    private async Task TryLoadModelsAsync(bool showErrors)
    {
        if (CurrentProvider is not { } provider)
        {
            return;
        }

        try
        {
            StatusTextBlock.Text = "در حال دریافت مدل‌ها...";

            SaveCurrentProviderSettings(provider);

            using CancellationTokenSource timeout =
                new(TimeSpan.FromSeconds(45));

            IReadOnlyList<AiModel> models =
                await _apiClient.LoadModelsAsync(
                    provider,
                    BaseUrlTextBox.Text.Trim(),
                    ApiKeyPasswordBox.Password.Trim(),
                    timeout.Token);

            ModelComboBox.ItemsSource = models;

            if (models.Count > 0)
            {
                AiModel? savedModel =
                    models.FirstOrDefault(
                        x => x.Id == _settings.LastModel);

                ModelComboBox.SelectedItem =
                    savedModel ?? models[0];

                StatusTextBlock.Text =
                    $"{models.Count} مدل دریافت شد.";
            }
            else
            {
                ModelComboBox.Text = _settings.LastModel;

                StatusTextBlock.Text =
                    "مدلی دریافت نشد. شناسهٔ مدل را دستی وارد کنید.";
            }
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text =
                $"دریافت مدل‌ها ناموفق بود: {exception.Message}";

            if (showErrors)
            {
                MessageBox.Show(
                    exception.Message,
                    "خطا در دریافت مدل‌ها",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void SendButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (CurrentProvider is not { } provider)
        {
            MessageBox.Show("ارائه‌دهنده را انتخاب کنید.");
            return;
        }

        string model = GetSelectedModelId();

        if (string.IsNullOrWhiteSpace(model))
        {
            MessageBox.Show(
                "یک مدل انتخاب کنید یا شناسهٔ آن را بنویسید.");
            return;
        }

        string userText = UserInputTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(userText))
        {
            return;
        }

        SaveCurrentProviderSettings(provider);

        if (_messages.Count == 0 &&
            !string.IsNullOrWhiteSpace(
                SystemPromptTextBox.Text))
        {
            _messages.Add(new ChatMessage(
                "system",
                SystemPromptTextBox.Text.Trim()));
        }

        _messages.Add(new ChatMessage(
            "user",
            userText));

        AppendTranscript(
            $"\n\n━━━━━━━━━━━━━━━━━━━━\nشما:\n{userText}\n\n" +
            $"{provider.Name} / {model}:\n");

        UserInputTextBox.Clear();

        SetBusy(true);

        _requestCancellation?.Dispose();
        _requestCancellation =
            new CancellationTokenSource();

        string assistantText = string.Empty;

        try
        {
            await _apiClient.StreamChatAsync(
                provider,
                BaseUrlTextBox.Text.Trim(),
                ApiKeyPasswordBox.Password.Trim(),
                model,
                _messages,
                chunk =>
                {
                    assistantText += chunk;

                    Dispatcher.Invoke(() =>
                    {
                        AppendTranscript(chunk);
                    });
                },
                _requestCancellation.Token);

            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                _messages.Add(new ChatMessage(
                    "assistant",
                    assistantText));
            }

            StatusTextBlock.Text = "پاسخ کامل شد.";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "پاسخ متوقف شد.";

            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                _messages.Add(new ChatMessage(
                    "assistant",
                    assistantText));
            }
        }
        catch (Exception exception)
        {
            AppendTranscript(
                $"\n\n[خطا: {exception.Message}]\n");

            StatusTextBlock.Text =
                $"خطا: {exception.Message}";

            MessageBox.Show(
                exception.Message,
                "خطای API",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string GetSelectedModelId()
    {
        if (ModelComboBox.SelectedItem is AiModel selectedModel)
        {
            return selectedModel.Id;
        }

        return ModelComboBox.Text.Trim();
    }

    private void AppendTranscript(string text)
    {
        TranscriptTextBox.AppendText(text);
        TranscriptTextBox.ScrollToEnd();
    }

    private void SetBusy(bool isBusy)
    {
        SendButton.IsEnabled = !isBusy;
        StopButton.IsEnabled = isBusy;
        ProviderComboBox.IsEnabled = !isBusy;
        ModelComboBox.IsEnabled = !isBusy;
    }

    private void StopButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _requestCancellation?.Cancel();
    }

    private void NewChatButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _requestCancellation?.Cancel();

        _messages.Clear();
        TranscriptTextBox.Clear();

        StatusTextBlock.Text = "گفتگوی جدید آغاز شد.";
    }

    private void OpenApiPageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (CurrentProvider?.ApiKeyPage is not { } url)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private async void UserInputTextBox_OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();

        base.OnClosed(e);
    }
}
