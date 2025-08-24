// INavigationService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Interfaces
{
    [SupportedOSPlatform("windows")]
    public interface INavigationService
    {
        /// <summary>
        /// Registers a page view type under a string key.
        /// </summary>
        void Register(string key, Type pageType);

        /// <summary>
        /// Navigate to the page previously registered under 'key'.
        /// </summary>
        void NavigateTo(string key);

        /// <summary>
        /// The currently displayed page (for binding to ContentControl).
        /// </summary>
        object? CurrentPageView { get; }

        /// <summary>
        /// Fired whenever CurrentPageView changes.
        /// </summary>
        event Action? CurrentPageChanged;
        event Action<string>? Navigated;
    }
}
