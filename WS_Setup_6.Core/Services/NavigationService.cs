// navigationservice.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.Core.Services
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _provider;
        private readonly Dictionary<string, Type> _routes = new();

        private object? _currentPageView;
        public object? CurrentPageView
        {
            get => _currentPageView;
            private set
            {
                _currentPageView = value;
                CurrentPageChanged?.Invoke();
            }
        }

        // existing event
        public event Action? CurrentPageChanged;

        // ← add this to satisfy INavigationService
        public event Action<string>? Navigated;

        public NavigationService(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void Register(string key, Type pageType)
        {
            _routes[key] = pageType;
        }

        public void NavigateTo(string key)
        {
            if (!_routes.TryGetValue(key, out var pageType))
                throw new ArgumentException($"No page registered with key '{key}'");

            var page = _provider.GetRequiredService(pageType);
            CurrentPageView = page;

            // fire your new event
            Navigated?.Invoke(key);
        }
    }
}