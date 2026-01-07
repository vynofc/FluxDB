using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluxDB.Models
{
    public class FileEntry : INotifyPropertyChanged
    {
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
            set { _extension = value; OnPropertyChanged(); }
        }

        public long Size
        {
            get { return _size; }
            set { _size = value; OnPropertyChanged(); }
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
            set { _isFolder = value; OnPropertyChanged(); }
        }

        public List<string> Tags { get; set; } = new List<string>();

        public string SizeDisplay
        {
            get
            {
                if (_isFolder) return "";
                if (_size < 1024) return _size + " B";
                if (_size < 1024 * 1024) return (_size / 1024.0).ToString("F1") + " KB";
                if (_size < 1024 * 1024 * 1024) return (_size / (1024.0 * 1024.0)).ToString("F1") + " MB";
                return (_size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
            }
        }

        /// <summary>
        /// Simple text-based icon
        /// </summary>
        public string Icon
        {
            get
            {
                if (_isFolder) return "[D]";
                
                var ext = (_extension ?? "").ToLower();
                
                // Images
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp" || ext == ".ico")
                    return "[I]";
                
                // Audio
                if (ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" || ext == ".ogg" || ext == ".wma" || ext == ".m4a")
                    return "[A]";
                
                // Video
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv" || ext == ".webm")
                    return "[V]";
                
                // Documents
                if (ext == ".pdf")
                    return "[P]";
                if (ext == ".doc" || ext == ".docx" || ext == ".rtf" || ext == ".odt" || ext == ".txt" || ext == ".md")
                    return "[T]";
                if (ext == ".xls" || ext == ".xlsx" || ext == ".csv" || ext == ".ods")
                    return "[X]";
                
                // Archives
                if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz")
                    return "[Z]";
                
                // Executables
                if (ext == ".exe" || ext == ".msi")
                    return "[E]";
                
                // Code files
                if (ext == ".cs" || ext == ".js" || ext == ".ts" || ext == ".py" || ext == ".java" || ext == ".cpp" || ext == ".c" || ext == ".h" ||
                    ext == ".html" || ext == ".css" || ext == ".xaml" || ext == ".xml" || ext == ".json" || ext == ".sql" || ext == ".php")
                    return "[C]";
                
                // Default
                return "[F]";
            }
        }

        /// <summary>
        /// Icon color based on file type
        /// </summary>
        public string IconColor
        {
            get
            {
                if (_isFolder) return "#DCB67A"; // Gold/Yellow for folders
                
                var ext = (_extension ?? "").ToLower();
                
                // Images - Purple
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp" || ext == ".ico")
                    return "#9B59B6";
                
                // Audio - Green
                if (ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" || ext == ".ogg" || ext == ".wma" || ext == ".m4a")
                    return "#2ECC71";
                
                // Video - Red
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv" || ext == ".webm")
                    return "#E74C3C";
                
                // PDF - Red
                if (ext == ".pdf")
                    return "#E74C3C";
                
                // Documents - Blue
                if (ext == ".doc" || ext == ".docx" || ext == ".rtf" || ext == ".odt" || ext == ".txt" || ext == ".md")
                    return "#3498DB";
                
                // Excel - Green
                if (ext == ".xls" || ext == ".xlsx" || ext == ".csv" || ext == ".ods")
                    return "#27AE60";
                
                // Archives - Orange
                if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz")
                    return "#E67E22";
                
                // Executables - Gray
                if (ext == ".exe" || ext == ".msi")
                    return "#95A5A6";
                
                // Code - Cyan
                if (ext == ".cs" || ext == ".js" || ext == ".ts" || ext == ".py" || ext == ".java" || ext == ".cpp" || ext == ".c" || ext == ".h" ||
                    ext == ".html" || ext == ".css" || ext == ".xaml" || ext == ".xml" || ext == ".json" || ext == ".sql" || ext == ".php")
                    return "#00CED1";
                
                // Default - Gray
                return "#BDC3C7";
            }
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
