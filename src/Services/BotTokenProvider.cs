using System;
using Telegram.Bot;

namespace TelegramChainBot.Services;

public sealed class BotTokenProvider
{
    private readonly object _lock = new();
    private string _token;
    private ITelegramBotClient? _cachedClient;

    public BotTokenProvider(string initialToken)
    {
        _token = initialToken;
    }

    public string Token
    {
        get
        {
            lock (_lock)
            {
                return _token;
            }
        }
    }

    public void UpdateToken(string newToken)
    {
        lock (_lock)
        {
            if (_token != newToken)
            {
                _token = newToken;
                _cachedClient = null; // Invalidate cached client
            }
        }
    }

    public ITelegramBotClient GetClient()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                throw new InvalidOperationException("Telegram Bot Token is not configured.");
            }

            if (_cachedClient == null)
            {
                _cachedClient = new TelegramBotClient(_token);
            }
            return _cachedClient;
        }
    }
}
