﻿using TailwindCSSIntellisense.Options;

namespace TailwindCSSIntellisense.Settings
{
    public class TailwindSettings
    {
        public string TailwindConfigurationFile { get; set; }
        public string DefaultOutputCssName { get; set; }
        public string TailwindCssFile { get; set; }
        public string TailwindOutputCssFile { get; set; }
        public bool EnableTailwindCss { get; set; }
        public BuildProcessOptions BuildType { get; set; }
        public string BuildScript { get; set; }
        public bool OverrideBuild { get; set; }
    }
}
