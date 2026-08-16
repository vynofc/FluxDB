using System.Windows.Media;
using Wpf.Ui.Controls;

namespace FluxDB.Models
{
    public class FileEntry
    {
        private static readonly Dictionary<string, (SymbolRegular Symbol, string Color)> IconLookup = new()
        {
            [".jpg"] = (SymbolRegular.Image24, "#9B59B6"), [".jpeg"] = (SymbolRegular.Image24, "#9B59B6"), [".png"] = (SymbolRegular.Image24, "#9B59B6"), [".gif"] = (SymbolRegular.Image24, "#9B59B6"),
            [".bmp"] = (SymbolRegular.Image24, "#9B59B6"), [".webp"] = (SymbolRegular.Image24, "#9B59B6"), [".ico"] = (SymbolRegular.Image24, "#9B59B6"),
            [".mp3"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".wav"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".flac"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".aac"] = (SymbolRegular.MusicNote224, "#2ECC71"),
            [".ogg"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".wma"] = (SymbolRegular.MusicNote224, "#2ECC71"), [".m4a"] = (SymbolRegular.MusicNote224, "#2ECC71"),
            [".mp4"] = (SymbolRegular.Video24, "#E74C3C"), [".avi"] = (SymbolRegular.Video24, "#E74C3C"), [".mkv"] = (SymbolRegular.Video24, "#E74C3C"), [".mov"] = (SymbolRegular.Video24, "#E74C3C"),
            [".wmv"] = (SymbolRegular.Video24, "#E74C3C"), [".flv"] = (SymbolRegular.Video24, "#E74C3C"), [".webm"] = (SymbolRegular.Video24, "#E74C3C"),
            [".pdf"] = (SymbolRegular.DocumentPdf24, "#E74C3C"),
            [".doc"] = (SymbolRegular.Document24, "#3498DB"), [".docx"] = (SymbolRegular.Document24, "#3498DB"), [".rtf"] = (SymbolRegular.Document24, "#3498DB"), [".odt"] = (SymbolRegular.Document24, "#3498DB"),
            [".txt"] = (SymbolRegular.Document24, "#3498DB"), [".md"] = (SymbolRegular.Document24, "#3498DB"),
            [".xls"] = (SymbolRegular.DocumentData24, "#27AE60"), [".xlsx"] = (SymbolRegular.DocumentData24, "#27AE60"), [".csv"] = (SymbolRegular.DocumentData24, "#27AE60"), [".ods"] = (SymbolRegular.DocumentData24, "#27AE60"),
            [".zip"] = (SymbolRegular.Box24, "#E67E22"), [".rar"] = (SymbolRegular.Box24, "#E67E22"), [".7z"] = (SymbolRegular.Box24, "#E67E22"), [".tar"] = (SymbolRegular.Box24, "#E67E22"), [".gz"] = (SymbolRegular.Box24, "#E67E22"),
            [".exe"] = (SymbolRegular.WindowConsole20, "#95A5A6"), [".msi"] = (SymbolRegular.WindowConsole20, "#95A5A6"),
            [".cs"] = (SymbolRegular.Code24, "#00CED1"), [".js"] = (SymbolRegular.Code24, "#00CED1"), [".ts"] = (SymbolRegular.Code24, "#00CED1"), [".py"] = (SymbolRegular.Code24, "#00CED1"),
            [".java"] = (SymbolRegular.Code24, "#00CED1"), [".cpp"] = (SymbolRegular.Code24, "#00CED1"), [".c"] = (SymbolRegular.Code24, "#00CED1"), [".h"] = (SymbolRegular.Code24, "#00CED1"),
            [".html"] = (SymbolRegular.Code24, "#00CED1"), [".css"] = (SymbolRegular.Code24, "#00CED1"), [".xaml"] = (SymbolRegular.Code24, "#00CED1"), [".xml"] = (SymbolRegular.Code24, "#00CED1"),
            [".json"] = (SymbolRegular.Code24, "#00CED1"), [".sql"] = (SymbolRegular.Code24, "#00CED1"), [".php"] = (SymbolRegular.Code24, "#00CED1"),
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

        public int Id { get => _id; set => _id = value; }
        public string Path { get => _path; set => _path = value; }
        public string Name { get => _name; set => _name = value; }
        public string Extension { get => _extension; set { _extension = value; InvalidateCache(); } }
        public long Size { get => _size; set { _size = value; _cachedSizeDisplay = null; } }
        public DateTime CreatedAt { get => _createdAt; set => _createdAt = value; }
        public DateTime ModifiedAt { get => _modifiedAt; set => _modifiedAt = value; }
        public bool Deleted { get => _deleted; set => _deleted = value; }
        public DateTime LastIndexedAt { get => _lastIndexedAt; set => _lastIndexedAt = value; }
        public string TagsText { get => _tagsText; set => _tagsText = value; }
        public string Note { get => _note; set => _note = value; }
        public bool IsFolder { get => _isFolder; set { _isFolder = value; InvalidateCache(); } }

        private SymbolRegular _cachedIconSymbol;
        private string _cachedSizeDisplay;
        private string _cachedTypeDisplay;

        public List<string> Tags { get; set; }

        public string SizeDisplay
        {
            get
            {
                if (_cachedSizeDisplay != null) return _cachedSizeDisplay;
                if (_isFolder) { _cachedSizeDisplay = ""; return _cachedSizeDisplay; }
                if (_size < 1024) _cachedSizeDisplay = _size + " B";
                else if (_size < 1024 * 1024) _cachedSizeDisplay = (_size / 1024.0).ToString("F1") + " KB";
                else if (_size < 1024 * 1024 * 1024) _cachedSizeDisplay = (_size / (1024.0 * 1024.0)).ToString("F1") + " MB";
                else _cachedSizeDisplay = (_size / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
                return _cachedSizeDisplay;
            }
        }

        public SymbolRegular IconSymbol
        {
            get
            {
                if (_cachedIconSymbol != default) return _cachedIconSymbol;
                return _cachedIconSymbol = _isFolder ? SymbolRegular.Folder24 : GetExtLookup().Symbol;
            }
        }

        public Brush IconColorBrush
        {
            get
            {
                return UiBrushes.GetIconColorBrush(_isFolder ? "#DCB67A" : GetExtLookup().Color);
            }
        }

        public string TypeDisplay
        {
            get
            {
                if (_cachedTypeDisplay != null) return _cachedTypeDisplay;
                if (_isFolder) { _cachedTypeDisplay = "Folder"; return _cachedTypeDisplay; }
                _cachedTypeDisplay = string.IsNullOrEmpty(_extension) ? "File" : _extension.TrimStart('.').ToUpper();
                return _cachedTypeDisplay;
            }
        }

        private void InvalidateCache()
        {
            _cachedIconSymbol = default;
            _cachedSizeDisplay = null;
            _cachedTypeDisplay = null;
        }

        private (SymbolRegular Symbol, string Color) GetExtLookup()
        {
            var ext = (_extension ?? "").ToLower();
            return IconLookup.TryGetValue(ext, out var value) ? value : (SymbolRegular.Document24, "#BDC3C7");
        }
    }

    internal static class UiBrushes
    {
        private static readonly Dictionary<string, Brush> _brushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        static UiBrushes()
        {
            Register("#9B59B6");
            Register("#2ECC71");
            Register("#E74C3C");
            Register("#3498DB");
            Register("#27AE60");
            Register("#E67E22");
            Register("#95A5A6");
            Register("#00CED1");
            Register("#BDC3C7");
            Register("#DCB67A");
            Register("#0078D4");
            Register("#107C10");
            Register("#D83B01");
            Register("#5C2D91");
            Register("#E81123");
            Register("#008272");
            Register("#E74856");
            Register("#8764B8");
            Register("#00B7C3");
            Register("#038387");
        }

        private static void Register(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            _brushes[hex] = brush;
        }

        public static Brush GetIconColorBrush(string hex)
        {
            lock (_lock)
            {
                if (_brushes.TryGetValue(hex, out var brush)) return brush;
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                _brushes[hex] = b;
                return b;
            }
        }

        public static Brush BreadcrumbBlue { get; } = CreateFrozen(0, 120, 212);

        private static Brush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
