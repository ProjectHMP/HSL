using ICSharpCode.AvalonEdit.Highlighting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using System.Collections.ObjectModel;

namespace HSL.Windows
{
    /// <summary>
    /// Interaction logic for IDE.xaml
    /// </summary>
    public partial class IDE : Window
    {

        public class FileMeta
        {
            public string File { get; private set; }
            public bool IsDirectory { get; private set; }
            public string FileName { get; set; }
            public FileMeta(string file, bool isDirectory)
            {
                File = file;
                FileName = Path.GetFileName(file);
                IsDirectory = isDirectory;
            }
        }

        private Dictionary<string, FileMeta> FileMetaCache;

        public ObservableCollection<FileMeta> CurrentDirectoryIndex { get; private set; }

        internal IDE(string file)
        {
            InitializeComponent();
            FileMetaCache = new Dictionary<string, FileMeta>();
            CurrentDirectoryIndex = new ObservableCollection<FileMeta>();
            txtCode.ShowLineNumbers = true;
            files.MouseDoubleClick += Files_MouseDoubleClick;
            UpdateTreeDirectory(Path.GetDirectoryName(file));
            LoadFile(file);
            DataContext = this;
        }

        private void Files_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if(files.SelectedItem != null && files.SelectedItem is FileMeta meta)
            {
                if (!meta.IsDirectory)
                {
                    LoadFile(meta.File);
                    return;
                }
                UpdateTreeDirectory(meta.File);
            }
        }

        private IHighlightingDefinition GetHighlightingDefinition(string file)
        {
            string ext = Path.GetExtension(file);
            switch (ext)
            {
                case ".nut":
                    ext = ".js";
                    break;
            }
            return HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }

        internal void LoadFile(string file)
        {
            if (File.Exists(file))
            {
                IHighlightingDefinition definition = GetHighlightingDefinition(System.IO.Path.GetExtension(file).ToLower());
                txtCode.SyntaxHighlighting = definition ?? txtCode.SyntaxHighlighting;
                txtCode.Load(file);
                Title = $"{nameof(IDE)} - {Path.GetFileName(file)}";
            }
        }

        internal void UpdateTreeDirectory(string directory)
        {
            string[] entries = Directory.GetFileSystemEntries(directory);
            CurrentDirectoryIndex.Clear();
            CurrentDirectoryIndex.Add(new FileMeta(Directory.GetParent(directory).FullName, true) { FileName = "< Back" });
            foreach(string entry in entries)
            {
                CurrentDirectoryIndex.Add(GetFileMeta(entry));
            }
        }

        internal FileMeta? GetFileMeta(string file)
        {
            if(!FileMetaCache.ContainsKey(file))
            {
                FileMetaCache.Add(file, new FileMeta(file, (File.GetAttributes(file) & FileAttributes.Directory) == FileAttributes.Directory));
            }
            return FileMetaCache[file];
        }


    }
}
