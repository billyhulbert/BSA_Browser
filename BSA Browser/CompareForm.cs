﻿using BSA_Browser.Properties;
using BSA_Browser.Sorting;
using SharpBSABA2;
using SharpBSABA2.Enums;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BSA_Browser
{
    public struct CompareItem
    {
        public string FullPath;
        public CompareType Type;

        public CompareItem(string fullPath, CompareType type)
        {
            this.FullPath = fullPath;
            this.Type = type;
        }
    }

    public enum CompareType
    {
        Removed = 1,
        Added = 2,
        Changed = 3,
        Identical = 4
    }

    public partial class CompareForm : Form
    {
        string CompareTextTemplate = string.Empty;

        public List<Archive> Archives { get; private set; } = new List<Archive>();
        public List<CompareItem> Files { get; private set; } = new List<CompareItem>();
        public List<CompareItem> FilteredFiles { get; private set; } = new List<CompareItem>();

        private NaturalStringComparer NaturalStringComparer = new NaturalStringComparer();

        public CompareForm()
        {
            InitializeComponent();

            CompareTextTemplate = this.lComparison.Text;

            chbFilterUnique.Checked = Settings.Default.CompareFilterUnique;
            chbFilterDifferent.Checked = Settings.Default.CompareFilterDifferent;
            chbFilterIdentical.Checked = Settings.Default.CompareFilterIdentical;
        }

        public CompareForm(ICollection<Archive> archives)
            : this()
        {
            if (archives?.Count > 0)
                this.Archives.AddRange(archives);
        }

        private void CompareForm_Load(object sender, EventArgs e)
        {
            foreach (var archive in this.Archives)
            {
                cbArchiveA.Items.Add(archive.FileName);
                cbArchiveB.Items.Add(archive.FileName);
            }

            this.lComparison.Text = string.Format(CompareTextTemplate, 0, 0, 0, 0, 0);
        }

        private void cbArchives_SelectedIndexChanged(object sender, EventArgs e)
        {
            var comboBox = sender as ComboBox;
            var type = sender == cbArchiveA ? lTypeA : lTypeB;
            var version = sender == cbArchiveA ? lVersionA : lVersionB;
            var fileCount = sender == cbArchiveA ? lFileCountA : lFileCountB;
            var chunks = sender == cbArchiveA ? lChunksA : lChunksB;
            var chunksLabel = sender == cbArchiveA ? lChunksAA : lChunksBB;
            var missingNameTable = sender == cbArchiveA ? lMissingNameTableA : lMissingNameTableB;

            if (comboBox.SelectedIndex < 0)
            {
                // Reset text and visibility
                type.Text = version.Text = fileCount.Text = chunks.Text = "-";
                chunks.Visible = chunksLabel.Visible = missingNameTable.Visible = false;
            }
            else
            {
                var archive = this.Archives[comboBox.SelectedIndex];
                type.Text = this.FormatType(archive.Type);
                version.Text = archive.VersionString;
                fileCount.Text = archive.FileCount.ToString();
                chunks.Text = archive.Chunks.ToString();
                chunks.Visible = chunksLabel.Visible = archive.Chunks > 0;
                missingNameTable.Visible = !archive.HasNameTable;
            }

            this.Compare();
        }

        private void lvArchive_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (this.FilteredFiles.Count <= e.ItemIndex)
                return;

            var file = this.FilteredFiles[e.ItemIndex];

            ListViewItem newItem;

            switch (file.Type)
            {
                case CompareType.Added:
                    newItem = new ListViewItem
                    {
                        UseItemStyleForSubItems = false
                    };
                    newItem.SubItems.Add(new ListViewItem.ListViewSubItem(newItem, file.FullPath)
                    {
                        ForeColor = Color.Green
                    });
                    break;
                case CompareType.Removed:
                    newItem = new ListViewItem(file.FullPath)
                    {
                        ForeColor = Color.Red
                    };
                    newItem.SubItems.Add(new ListViewItem.ListViewSubItem());
                    break;
                case CompareType.Changed:
                    newItem = new ListViewItem(file.FullPath)
                    {
                        ForeColor = Color.Blue
                    };
                    newItem.SubItems.Add(new ListViewItem.ListViewSubItem(newItem, file.FullPath));
                    break;
                case CompareType.Identical:
                    newItem = new ListViewItem(file.FullPath)
                    {
                        ForeColor = Color.Gray
                    };
                    newItem.SubItems.Add(new ListViewItem.ListViewSubItem(newItem, file.FullPath));
                    break;
                default:
                    throw new Exception("Unknown CompareType");
            }

            newItem.ToolTipText = file.FullPath;

            e.Item = newItem;
        }

        private void lFilters_CheckedChanged(object sender, EventArgs e)
        {
            lvArchive.BeginUpdate();
            this.Filter();
            lvArchive.VirtualListSize = this.FilteredFiles.Count;
            lvArchive.Invalidate();
            lvArchive.EndUpdate();

            Settings.Default.CompareFilterUnique = chbFilterUnique.Checked;
            Settings.Default.CompareFilterDifferent = chbFilterDifferent.Checked;
            Settings.Default.CompareFilterIdentical = chbFilterIdentical.Checked;
        }

        private void Filter()
        {
            this.FilteredFiles.Clear();

            var types = GetFilteredTypes();

            foreach (var file in Files)
                if (types.Contains(file.Type))
                    this.FilteredFiles.Add(file);
        }

        private CompareType[] GetFilteredTypes()
        {
            var types = new List<CompareType>();
            if (chbFilterUnique.Checked)
            {
                types.Add(CompareType.Added);
                types.Add(CompareType.Removed);
            }
            if (chbFilterDifferent.Checked)
                types.Add(CompareType.Changed);
            if (chbFilterIdentical.Checked)
                types.Add(CompareType.Identical);

            return types.ToArray();
        }

        private void Compare()
        {
            if (cbArchiveA.SelectedIndex < 0 || cbArchiveB.SelectedIndex < 0)
                return;

            if (cbArchiveA.SelectedIndex == cbArchiveB.SelectedIndex)
            {
                // Same archive, don't compare anything but still show info and files
                this.CompareSameArchive();
                return;
            }

            var archA = this.Archives[cbArchiveA.SelectedIndex];
            var archB = this.Archives[cbArchiveB.SelectedIndex];

            this.SetCompareColor(lTypeA, lTypeB, archA.Type != archB.Type);
            this.SetCompareColor(lVersionA, lVersionB, archA.VersionString != archB.VersionString);
            this.SetCompareColor(lFileCountA, lFileCountB, archA.FileCount != archB.FileCount);
            this.SetCompareColor(lChunksA, lChunksB, archA.Chunks != archB.Chunks);

            var archAFileList = archA.Files.ToDictionary(x => x.FullPath.ToLower());
            var archBFileList = archB.Files.ToDictionary(x => x.FullPath.ToLower());

            // Merge file list, ignore duplicates
            var dict = archAFileList.Keys.ToDictionary(x => x);
            foreach (var file in archBFileList.Keys) dict[file] = file;
            var filelist = dict.Values.ToList();

            lvArchive.BeginUpdate();
            this.Files.Clear();

            var epA = archA.CreateSharedParams(true, false);
            var epB = archB.CreateSharedParams(true, false);

            foreach (var file in filelist)
            {
                if (archAFileList.ContainsKey(file) && !archBFileList.ContainsKey(file))
                {
                    // File appears in left archive only
                    this.Files.Add(new CompareItem(archAFileList[file].FullPath, CompareType.Removed));
                }
                else if (!archAFileList.ContainsKey(file) && archBFileList.ContainsKey(file))
                {
                    // File appears in right archive only
                    this.Files.Add(new CompareItem(archBFileList[file].FullPath, CompareType.Added));
                }
                else
                {
                    epA.Reader.BaseStream.Position = (long)archAFileList[file].Offset;
                    epB.Reader.BaseStream.Position = (long)archBFileList[file].Offset;

                    if (archAFileList[file].GetSizeInArchive(epA) == archBFileList[file].GetSizeInArchive(epB)
                        && CompareStreams(epA.Reader.BaseStream, epB.Reader.BaseStream, archAFileList[file].GetSizeInArchive(epA)))
                    {
                        // Files are identical
                        this.Files.Add(new CompareItem(archAFileList[file].FullPath, CompareType.Identical));
                    }
                    else
                    {
                        // Files are different
                        this.Files.Add(new CompareItem(archAFileList[file].FullPath, CompareType.Changed));
                    }
                }
            }

            epA.Dispose();
            epB.Dispose();

            this.Files.Sort((x, y) =>
            {
                int comparison = ((int)x.Type).CompareTo((int)y.Type);
                if (comparison != 0)
                    return comparison;

                return NaturalStringComparer.Compare(x.FullPath, y.FullPath);
            });

            this.Filter();
            lvArchive.VirtualListSize = this.FilteredFiles.Count;
            lvArchive.Invalidate();
            lvArchive.EndUpdate();

            this.lComparison.Text = string.Format(CompareTextTemplate,
                this.Files.Count(x => x.Type == CompareType.Added),
                this.Files.Count(x => x.Type == CompareType.Removed),
                this.Files.Count(x => x.Type == CompareType.Changed),
                archAFileList.Count,
                archBFileList.Count);
        }

        private void CompareSameArchive()
        {
            SetCompareColor(lTypeA, lTypeB, false);
            SetCompareColor(lVersionA, lVersionB, false);
            SetCompareColor(lFileCountA, lFileCountB, false);
            SetCompareColor(lChunksA, lChunksB, false);

            lvArchive.BeginUpdate();
            this.Files.Clear();

            var archive = this.Archives[cbArchiveA.SelectedIndex];
            foreach (var file in archive.Files)
            {
                // Files are identical
                this.Files.Add(new CompareItem(file.FullPath, CompareType.Identical));
            }

            this.Files.Sort((x, y) =>
            {
                return NaturalStringComparer.Compare(x.FullPath, y.FullPath);
            });

            this.Filter();
            lvArchive.VirtualListSize = this.FilteredFiles.Count;
            lvArchive.Invalidate();
            lvArchive.EndUpdate();

            this.lComparison.Text = string.Format(CompareTextTemplate,
                this.Files.Count(x => x.Type == CompareType.Added),
                this.Files.Count(x => x.Type == CompareType.Removed),
                this.Files.Count(x => x.Type == CompareType.Changed),
                archive.Files.Count,
                archive.Files.Count);
        }

        public void AddArchive(Archive archive)
        {
            this.Archives.Add(archive);

            cbArchiveA.Items.Add(archive.FileName);
            cbArchiveB.Items.Add(archive.FileName);
        }

        public void RemoveArchive(Archive archive)
        {
            this.Archives.Remove(archive);

            cbArchiveA.Items.Remove(archive.FileName);
            cbArchiveB.Items.Remove(archive.FileName);
        }

        private void SetCompareColor(Control a, Control b, bool comparison)
        {
            a.ForeColor = comparison ? Color.Red : SystemColors.ControlText;
            b.ForeColor = comparison ? Color.Green : SystemColors.ControlText;
        }

        private string FormatType(ArchiveTypes type)
        {
            switch (type)
            {
                case ArchiveTypes.BA2_DX10: return "BA2 Texture";
                case ArchiveTypes.BA2_GNMF: return "BA2 Texture (GNF)";
                case ArchiveTypes.BA2_GNRL: return "BA2 General";
                case ArchiveTypes.BSA: return "BSA";
                case ArchiveTypes.BSA_MW: return "BSA Morrowind";
                case ArchiveTypes.BSA_SE: return "BSA Special Edition";
                case ArchiveTypes.DAT_F2: return "DAT Fallout 2";
                default: return string.Empty;
            }
        }

        private bool CompareStreams(Stream a, Stream b, ulong length)
        {
            int Buffer = 1024 * 10;

            byte[] one = new byte[Buffer];
            byte[] two = new byte[Buffer];
            ulong read = 0;

            while (true)
            {
                // Check if read will be more than length
                if (read + (ulong)Buffer > length)
                {
                    // Reduce buffer by the difference
                    var reduce = (read + (ulong)Buffer) - length;
                    Buffer -= (int)reduce;
                }

                int len1 = a.Read(one, 0, Buffer);
                int len2 = b.Read(two, 0, Buffer);
                int index = 0;

                read += (ulong)len1;

                while (index < len1 && index < len2)
                {
                    if (one[index] != two[index]) return false;
                    index++;
                }
                if (read == length) break; // Max length reached
            }
            return true;
        }
    }
}
