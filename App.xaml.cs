using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FluxDB
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    public partial class App : Application
    {
        public static bool IsUpdateAvailable { get; set; } = false;
        public static string AvailableVersion { get; set; } = "";
        public static bool IsUpdateSkipped { get; set; } = false;
    }
}
