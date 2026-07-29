using System;
using System.Collections.Generic;
using FluxDB.Models;

namespace FluxDB.Plugin
{
    public class FileEventArgs : EventArgs
    {
        public FileEntry File { get; set; }
        public string FolderPath { get; set; }

        public FileEventArgs(FileEntry file, string folderPath)
        {
            File = file;
            FolderPath = folderPath;
        }
    }

    public class SearchEventArgs : EventArgs
    {
        public string Query { get; set; }
        public List<FileEntry> Results { get; set; }
        public string FolderPath { get; set; }

        public SearchEventArgs(string query, List<FileEntry> results, string folderPath)
        {
            Query = query;
            Results = results;
            FolderPath = folderPath;
        }
    }
}