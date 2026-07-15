using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;

namespace TelegramChainBot.Services;

public sealed class DynamicTelegramBotClient : ITelegramBotClient
{
    private readonly BotTokenProvider _tokenProvider;

    public DynamicTelegramBotClient(BotTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    private ITelegramBotClient Client => _tokenProvider.GetClient();

    public bool LocalBotServer => Client.LocalBotServer;

    public long BotId => Client.BotId;

    public TimeSpan Timeout
    {
        get => Client.Timeout;
        set => Client.Timeout = value;
    }

    public IExceptionParser ExceptionsParser
    {
        get => Client.ExceptionsParser;
        set => Client.ExceptionsParser = value;
    }

    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
    {
        add => Client.OnMakingApiRequest += value;
        remove => Client.OnMakingApiRequest -= value;
    }

    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
    {
        add => Client.OnApiResponseReceived += value;
        remove => Client.OnApiResponseReceived -= value;
    }

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Client.SendRequest(request, cancellationToken);
    }

    public Task<TResponse> MakeRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Client.MakeRequest(request, cancellationToken);
    }

    public Task<TResponse> MakeRequestAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Client.MakeRequestAsync(request, cancellationToken);
    }

    public Task<bool> TestApi(CancellationToken cancellationToken = default)
    {
        return Client.TestApi(cancellationToken);
    }

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
    {
        return Client.DownloadFile(filePath, destination, cancellationToken);
    }
}
