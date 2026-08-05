using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluxDB.Models
{
    public class FileEntry : INotifyPropertyChanged
    {
        private static readonly Dictionary<string, (string Icon, string Color)> IconLookup = new Dictionary<string, (string, string)>
        {
            [".jpg"] = ("\uEB9F", "#9B59B6"), [".jpeg"] = ("\uEB9F", "#9B59B6"), [".png"] = ("\uEB9F", "#9B59B6"), [".gif"] = ("\uEB9F", "#9B59B6"),
            [".bmp"] = ("\uEB9F", "#9B59B6"), [".webp"] = ("\uEB9F", "#9B59B6"), [".ico"] = ("\uEB9F", "#9B59B6"),
            [".mp3"] = ("\uE189", "#2ECC71"), [".wav"] = ("\uE189", "#2ECC71"), [".flac"] = ("\uE189", "#2ECC71"), [".aac"] = ("\uE189", "#2ECC71"),
            [".ogg"] = ("\uE189", "#2ECC71"), [".wma"] = ("\uE189", "#2ECC71"), [".m4a"] = ("\uE189", "#2ECC71"),
            [".mp4"] = ("\uE116", "#E74C3C"), [".avi"] = ("\uE116", "#E74C3C"), [".mkv"] = ("\uE116", "#E74C3C"), [".mov"] = ("\uE116", "#E74C3C"),
            [".wmv"] = ("\uE116", "#E74C3C"), [".flv"] = ("\uE116", "#E74C3C"), [".webm"] = ("\uE116", "#E74C3C"),
            [".pdf"] = ("\uE162", "#E74C3C"),
            [".doc"] = ("\uE132", "#3498DB"), [".docx"] = ("\uE132", "#3498DB"), [".rtf"] = ("\uE132", "#3498DB"), [".odt"] = ("\uE132", "#3498DB"),
            [".txt"] = ("\uE132", "#3498DB"), [".md"] = ("\uE132", "#3498DB"),
            [".xls"] = ("\uE1D2", "#27AE60"), [".xlsx"] = ("\uE1D2", "#27AE60"), [".csv"] = ("\uE1D2", "#27AE60"), [".ods"] = ("\uE1D2", "#27AE60"),
            [".zip"] = ("\uF012", "#E67E22"), [".rar"] = ("\uF012", "#E67E22"), [".7z"] = ("\uF012", "#E67E22"), [".tar"] = ("\uF012", "#E67E22"), [".gz"] = ("\uF012", "#E67E22"),
            [".exe"] = ("\uE71D", "#95A5A6"), [".msi"] = ("\uE71D", "#95A5A6"),
            [".cs"] = ("\uE943", "#00CED1"), [".js"] = ("\uE943", "#00CED1"), [".ts"] = ("\uE943", "#00CED1"), [".py"] = ("\uE943", "#00CED1"),
            [".java"] = ("\uE943", "#00CED1"), [".cpp"] = ("\uE943", "#00CED1"), [".c"] = ("\uE943", "#00CED1"), [".h"] = ("\uE943", "#00CED1"),
            [".html"] = ("\uE943", "#00CED1"), [".css"] = ("\uE943", "#00CED1"), [".xaml"] = ("\uE943", "#00CED1"), [".xml"] = ("\uE943", "#00CED1"),
            [".json"] = ("\uE943", "#00CED1"), [".sql"] = ("\uE943", "#00CED1"), [".php"] = ("\uE943", "#00CED1"),
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
                return _cachedIcon = _isFolder ? "\uE8B7" : GetExtLookup().Icon;
            }
        }

        /// <summary>
        /// Icon color based on file type
        /// </summary>
        public string IconColor
        {
            get
            {
                if (_cacheValid && _cachedIconColor != null) return _cachedIconColor;
                return _cachedIconColor = _isFolder ? "#DCB67A" : GetExtLookup().Color;
            }
        }

        private (string Icon, string Color) GetExtLookup()
        {
            var ext = (_extension ?? "").ToLower();
            return IconLookup.TryGetValue(ext, out var value) ? value : ("\uE160", "#BDC3C7");
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
