using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluxDB.Models
{
    /// <summary>
    /// Represents a file entry in the database
    /// </summary>
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
            set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }

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

        public string Icon
        {
            get
            {
                if (_isFolder) return "??";
                var ext = (_extension ?? "").ToLower();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp") return "???";
                if (ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac") return "??";
                if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov") return "??";
                if (ext == ".pdf") return "??";
                if (ext == ".doc" || ext == ".docx") return "??";
                if (ext == ".xls" || ext == ".xlsx") return "??";
                if (ext == ".zip" || ext == ".rar" || ext == ".7z") return "??";
                if (ext == ".exe" || ext == ".msi") return "??";
                if (ext == ".cs" || ext == ".js" || ext == ".py" || ext == ".html") return "??";
                return "??";
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
