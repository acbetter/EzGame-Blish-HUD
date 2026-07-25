using System;
using System.ComponentModel;
using System.Globalization;

namespace Blish_HUD.Graphics {

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    internal sealed class LocalizedGraphicsDescriptionAttribute : DescriptionAttribute {

        private readonly string _resourceKey;

        public LocalizedGraphicsDescriptionAttribute(string resourceKey, string fallback)
            : base(fallback) {
            _resourceKey = resourceKey;
        }

        public override string Description =>
            Strings.GameServices.GraphicsService.ResourceManager.GetString(
                _resourceKey,
                CultureInfo.CurrentUICulture
            ) ?? base.Description;

    }

}
