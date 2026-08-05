using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluxDB.Models
{
    public class FileEntry : INotifyPropertyChanged
    {
        private static readonly Dictionary<string, string> IconMap = new Dictionary<string, string>
        {
            [".jpg"] = "\uEB9F", [".jpeg"] = "\uEB9F", [".png"] = "\uEB9F", [".gif"] = "\uEB9F",
            [".bmp"] = "\uEB9F", [".webp"] = "\uEB9F", [".ico"] = "\uEB9F",
            [".mp3"] = "\uE189", [".wav"] = "\uE189", [".flac"] = "\uE189", [".aac"] = "\uE189",
            [".ogg"] = "\uE189", [".wma"] = "\uE189", [".m4a"] = "\uE189",
            [".mp4"] = "\uE116", [".avi"] = "\uE116", [".mkv"] = "\uE116", [".mov"] = "\uE116",
            [".wmv"] = "\uE116", [".flv"] = "\uE116", [".webm"] = "\uE116",
            [".pdf"] = "\uE162",
            [".doc"] = "\uE132", [".docx"] = "\uE132", [".rtf"] = "\uE132", [".odt"] = "\uE132",
            [".txt"] = "\uE132", [".md"] = "\uE132",
            [".xls"] = "\uE1D2", [".xlsx"] = "\uE1D2", [".csv"] = "\uE1D2", [".ods"] = "\uE1D2",
            [".zip"] = "\uF012", [".rar"] = "\uF012", [".7z"] = "\uF012", [".tar"] = "\uF012", [".gz"] = "\uF012",
            [".exe"] = "\uE71D", [".msi"] = "\uE71D",
            [".cs"] = "\uE943", [".js"] = "\uE943", [".ts"] = "\uE943", [".py"] = "\uE943",
            [".java"] = "\uE943", [".cpp"] = "\uE943", [".c"] = "\uE943", [".h"] = "\uE943",
            [".html"] = "\uE943", [".css"] = "\uE943", [".xaml"] = "\uE943", [".xml"] = "\uE943",
            [".json"] = "\uE943", [".sql"] = "\uE943", [".php"] = "\uE943",
        };

        private static readonly Dictionary<string, string> IconColorMap = new Dictionary<string, string>
        {
            [".jpg"] = "#9B59B6", [".jpeg"] = "#9B59B6", [".png"] = "#9B59B6", [".gif"] = "#9B59B6",
            [".bmp"] = "#9B59B6", [".webp"] = "#9B59B6", [".ico"] = "#9B59B6",
            [".mp3"] = "#2ECC71", [".wav"] = "#2ECC71", [".flac"] = "#2ECC71", [".aac"] = "#2ECC71",
            [".ogg"] = "#2ECC71", [".wma"] = "#2ECC71", [".m4a"] = "#2ECC71",
            [".mp4"] = "#E74C3C", [".avi"] = "#E74C3C", [".mkv"] = "#E74C3C", [".mov"] = "#E74C3C",
            [".wmv"] = "#E74C3C", [".flv"] = "#E74C3C", [".webm"] = "#E74C3C",
            [".pdf"] = "#E74C3C",
            [".doc"] = "#3498DB", [".docx"] = "#3498DB", [".rtf"] = "#3498DB", [".odt"] = "#3498DB",
            [".txt"] = "#3498DB", [".md"] = "#3498DB",
            [".xls"] = "#27AE60", [".xlsx"] = "#27AE60", [".csv"] = "#27AE60", [".ods"] = "#27AE60",
            [".zip"] = "#E67E22", [".rar"] = "#E67E22", [".7z"] = "#E67E22", [".tar"] = "#E67E22", [".gz"] = "#E67E22",
            [".exe"] = "#95A5A6", [".msi"] = "#95A5A6",
            [".cs"] = "#00CED1", [".js"] = "#00CED1", [".ts"] = "#00CED1", [".py"] = "#00CED1",
            [".java"] = "#00CED1", [".cpp"] = "#00CED1", [".c"] = "#00CED1", [".h"] = "#00CED1",
            [".html"] = "#00CED1", [".css"] = "#00CED1", [".xaml"] = "#00CED1", [".xml"] = "#00CED1",
            [".json"] = "#00CED1", [".sql"] = "#00CED1", [".php"] = "#00CED1",
        };
        private int _id;
        private string _path;
        private string _name;
        private string _extension;
        private long _size;
        private DateTime _createdAt;
        private DateTime _modifiedAt;
        private bool _deleted;
        private DateTime _lastIndexedAt;
        private string _tagsText;
        private string _note;
        private bool _isFolder;
        private string _cachedIcon;
        private string _cachedIconColor;
        private string _cachedSizeDisplay;
        private bool _cacheValid;

        public int Id
        {
            get { return _id; }
            set { _id = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get { return _path; }
            set { _path = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; OnPropertyChanged(); }
        }

        public string Extension
        {
            get { return _extension; }
            set { _extension = value; _cacheValid = false; OnPropertyChanged(); }
        }

        public long Size
        {
            get { return _size; }
            set { _size = value; _cacheValid = false; OnPropertyChanged(); }
        }

        public DateTime CreatedAt
        {
            get { return _createdAt; }
            set { _createdAt = value; OnPropertyChanged(); }
        }

        public DateTime ModifiedAt
        {
            get { return _modifiedAt; }
            set { _modifiedAt = value; OnPropertyChanged(); }
        }

        public bool Deleted
        {
            get { return _deleted; }
            set { _deleted = value; OnPropertyChanged(); }
        }

        public DateTime LastIndexedAt
        {
            get { return _lastIndexedAt; }
            set { _lastIndexedAt = value; OnPropertyChanged(); }
        }

        public string TagsText
        {
            get { return _tagsText; }
            set { _tagsText = value; OnPropertyChanged(); }
        }

        public string Note
        {
            get { return _note; }
            set { _note = value; OnPropertyChanged(); }
        }

        public bool IsFolder
        {
            get { return _isFolder; }
            set { _isFolder = value; _cacheValid = false; OnPropertyChanged(); }
        }

        public List<string> Tags { get; set; } = new List<string>();

        public string SizeDisplay
        {
            get
            {
                if (_cacheValid && _cachedSizeDisplay != null) return _cachedSizeDisplay;
                if (_isFolder) { _cachedSizeDisplay = ""; return _cachedSizeDisplay; }
                if (_size < 1024) _cachedSizeDisplay = _size + " B";
                else if (_size < 1024 * 1024) _cachedSizeDisplay = (_size / 1024.0).ToString("F1") + " KB";
                else if (_size < 1024 * 1024 * 1024) _cachedSizeDisplay = (_size / (1024.0 * 1024.0)).ToString("F1") + " MB";
                else _cachedSizeDisplay = (_size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
                return _cachedSizeDisplay;
            }
        }

        /// <summary>
        /// Simple text-based icon
        /// </summary>
        public string Icon
        {
            get
            {
                if (_cacheValid && _cachedIcon != null) return _cachedIcon;
                _cachedIcon = ComputeIcon();
                return _cachedIcon;
            }
        }

        private string ComputeIcon()
        {
            if (_isFolder) return "\uE8B7";
            var ext = (_extension ?? "").ToLower();
            return IconMap.TryGetValue(ext, out var icon) ? icon : "\uE160";
        }

        /// <summary>
        /// Icon color based on file type
        /// </summary>
        public string IconColor
        {
            get
            {
                if (_cacheValid && _cachedIconColor != null) return _cachedIconColor;
                _cachedIconColor = ComputeIconColor();
                return _cachedIconColor;
            }
        }

        private string ComputeIconColor()
        {
            if (_isFolder) return "#DCB67A";
            var ext = (_extension ?? "").ToLower();
            return IconColorMap.TryGetValue(ext, out var color) ? color : "#BDC3C7";
        }

        public string TypeDisplay
        {
            get
            {
                if (_isFolder) return "Folder";
                return string.IsNullOrEmpty(_extension) ? "File" : _extension.TrimStart('.').ToUpper();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
