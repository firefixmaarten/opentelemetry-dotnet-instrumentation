using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Datadog.Trace.Ci
{
    /// <summary>
    /// Git information class
    /// </summary>
    internal class GitInfo
    {
        private GitInfo()
        {
        }

        /// <summary>
        /// Gets Source root
        /// </summary>
        public string SourceRoot { get; private set; }

        /// <summary>
        /// Gets Repository
        /// </summary>
        public string Repository { get; private set; }

        /// <summary>
        /// Gets Branch
        /// </summary>
        public string Branch { get; private set; }

        /// <summary>
        /// Gets Commit
        /// </summary>
        public string Commit { get; private set; }

        /// <summary>
        /// Gets Author Name
        /// </summary>
        public string AuthorName { get; private set; }

        /// <summary>
        /// Gets Author Email
        /// </summary>
        public string AuthorEmail { get; private set; }

        /// <summary>
        /// Gets Author Date
        /// </summary>
        public DateTimeOffset? AuthorDate { get; private set; }

        /// <summary>
        /// Gets Committer Name
        /// </summary>
        public string CommitterName { get; private set; }

        /// <summary>
        /// Gets Committer Email
        /// </summary>
        public string CommitterEmail { get; private set; }

        /// <summary>
        /// Gets Committer Date
        /// </summary>
        public DateTimeOffset? CommitterDate { get; private set; }

        /// <summary>
        /// Gets PGP Signature
        /// </summary>
        public string PgpSignature { get; private set; }

        /// <summary>
        /// Gets Commit Message
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Gets a GitInfo from a folder
        /// </summary>
        /// <param name="folder">Target folder to retrieve the git info</param>
        /// <returns>Git info</returns>
        public static GitInfo GetFrom(string folder)
        {
            return GetFrom(new DirectoryInfo(folder));
        }

        /// <summary>
        /// Gets a GitInfo from the current folder or assembly attribute
        /// </summary>
        /// <returns>Git info</returns>
        public static GitInfo GetCurrent()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo gitDirectory = GetParentGitFolder(baseDirectory) ?? GetParentGitFolder(Environment.CurrentDirectory);
            return GetFrom(gitDirectory);
        }

        private static GitInfo GetFrom(DirectoryInfo gitDirectory)
        {
            if (gitDirectory == null)
            {
                return new GitInfo();
            }

            GitInfo gitInfo = new GitInfo();

            try
            {
                gitInfo.SourceRoot = gitDirectory.Parent?.FullName;

                // Get Git commit
                string headPath = Path.Combine(gitDirectory.FullName, "HEAD");
                if (File.Exists(headPath))
                {
                    string head = File.ReadAllText(headPath).Trim();

                    // Symbolic Reference
                    if (head.StartsWith("ref:"))
                    {
                        gitInfo.Branch = head.Substring(4).Trim();

                        string refPath = Path.Combine(gitDirectory.FullName, gitInfo.Branch);
                        string infoRefPath = Path.Combine(gitDirectory.FullName, "info", "refs");

                        if (File.Exists(refPath))
                        {
                            // Get the commit from the .git/{refPath} file.
                            gitInfo.Commit = File.ReadAllText(refPath).Trim();
                        }
                        else if (File.Exists(infoRefPath))
                        {
                            // Get the commit from the .git/info/refs file.
                            string[] lines = File.ReadAllLines(infoRefPath);
                            foreach (string line in lines)
                            {
                                string[] hashRef = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                if (hashRef[1] == gitInfo.Branch)
                                {
                                    gitInfo.Commit = hashRef[0];
                                }
                            }
                        }
                    }
                    else
                    {
                        // Hash reference
                        gitInfo.Commit = head;
                    }
                }

                // Process Git Config
                string configPath = Path.Combine(gitDirectory.FullName, "config");
                List<ConfigItem> lstConfigs = GetConfigItems(configPath);
                if (lstConfigs != null && lstConfigs.Count > 0)
                {
                    var remote = "origin";

                    var branchItem = lstConfigs.Find(i => i.Type == "branch" && i.Merge == gitInfo.Branch);
                    if (branchItem != null)
                    {
                        gitInfo.Branch = branchItem.Name;
                        remote = branchItem.Remote;
                    }

                    var remoteItem = lstConfigs.Find(i => i.Type == "remote" && i.Name == remote);
                    if (remoteItem != null)
                    {
                        gitInfo.Repository = remoteItem.Url;
                    }
                }

                // Get author and committer data
                if (!string.IsNullOrEmpty(gitInfo.Commit))
                {
                    string folder = gitInfo.Commit.Substring(0, 2);
                    string file = gitInfo.Commit.Substring(2);
                    string objectFilePath = Path.Combine(gitDirectory.FullName, "objects", folder, file);
                    if (File.Exists(objectFilePath))
                    {
                        // Load and parse object file
                        if (GitCommitObject.TryGetFromObjectFile(objectFilePath, out var commitObject))
                        {
                            gitInfo.AuthorDate = commitObject.AuthorDate;
                            gitInfo.AuthorEmail = commitObject.AuthorEmail;
                            gitInfo.AuthorName = commitObject.AuthorName;
                            gitInfo.CommitterDate = commitObject.CommitterDate;
                            gitInfo.CommitterEmail = commitObject.CommitterEmail;
                            gitInfo.CommitterName = commitObject.CommitterName;
                            gitInfo.Message = commitObject.Message;
                            gitInfo.PgpSignature = commitObject.PgpSignature;
                        }
                    }
                    else
                    {
                        // Search git object file from the pack files
                        string packFolder = Path.Combine(gitDirectory.FullName, "objects", "pack");
                        string[] files = Directory.GetFiles(packFolder, "*.idx", SearchOption.TopDirectoryOnly);
                        foreach (string idxFile in files)
                        {
                            if (GitPackageOffset.TryGetPackageOffset(idxFile, gitInfo.Commit, out var packageOffset))
                            {
                                if (GitCommitObject.TryGetFromPackageOffset(packageOffset, out var commitObject))
                                {
                                    gitInfo.AuthorDate = commitObject.AuthorDate;
                                    gitInfo.AuthorEmail = commitObject.AuthorEmail;
                                    gitInfo.AuthorName = commitObject.AuthorName;
                                    gitInfo.CommitterDate = commitObject.CommitterDate;
                                    gitInfo.CommitterEmail = commitObject.CommitterEmail;
                                    gitInfo.CommitterName = commitObject.CommitterName;
                                    gitInfo.Message = commitObject.Message;
                                    gitInfo.PgpSignature = commitObject.PgpSignature;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return gitInfo;
        }

        private static DirectoryInfo GetParentGitFolder(string innerFolder)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(innerFolder);
            while (dirInfo != null)
            {
                DirectoryInfo[] gitDirectories = dirInfo.GetDirectories(".git");
                if (gitDirectories.Length > 0)
                {
                    foreach (var gitDir in gitDirectories)
                    {
                        if (gitDir.Name == ".git")
                        {
                            return gitDir;
                        }
                    }
                }

                dirInfo = dirInfo.Parent;
            }

            return null;
        }

        private static List<ConfigItem> GetConfigItems(string configFile)
        {
            if (!File.Exists(configFile))
            {
                return null;
            }

            var lstConfig = new List<ConfigItem>();
            ConfigItem currentItem = null;

            var regex = new Regex("^\\[(.*) \\\"(.*)\\\"\\]");
            string[] lines = File.ReadAllLines(configFile);
            foreach (string line in lines)
            {
                if (line[0] == '\t')
                {
                    if (currentItem != null)
                    {
                        string[] keyValue = line.Substring(1).Split(new string[] { " = " }, StringSplitOptions.RemoveEmptyEntries);
                        switch (keyValue[0])
                        {
                            case "url":
                                currentItem.Url = keyValue[1];
                                break;
                            case "remote":
                                currentItem.Remote = keyValue[1];
                                break;
                            case "merge":
                                currentItem.Merge = keyValue[1];
                                break;
                        }
                    }

                    continue;
                }

                var match = regex.Match(line);
                if (match.Success)
                {
                    if (currentItem != null)
                    {
                        lstConfig.Add(currentItem);
                    }

                    currentItem = new ConfigItem
                    {
                        Type = match.Groups[1].Value,
                        Name = match.Groups[2].Value
                    };
                }
            }

            return lstConfig;
        }

        internal readonly struct GitCommitObject
        {
            public readonly string Tree;
            public readonly string Parent;
            public readonly string AuthorName;
            public readonly string AuthorEmail;
            public readonly DateTimeOffset? AuthorDate;
            public readonly string CommitterName;
            public readonly string CommitterEmail;
            public readonly DateTimeOffset? CommitterDate;
            public readonly string PgpSignature;
            public readonly string Message;

            private const string TreePrefix = "tree ";
            private const string ParentPrefix = "parent ";
            private const string AuthorPrefix = "author ";
            private const string CommitterPrefix = "committer ";
            private const string GpgSigPrefix = "gpgsig ";
            private const long UnixEpochTicks = TimeSpan.TicksPerDay * 719162; // 621,355,968,000,000,000

            private static readonly byte[] _commitByteArray = Encoding.UTF8.GetBytes("commit");

            private GitCommitObject(string content)
            {
                Tree = null;
                Parent = null;
                AuthorName = null;
                AuthorEmail = null;
                AuthorDate = null;
                CommitterName = null;
                CommitterEmail = null;
                CommitterDate = null;
                PgpSignature = null;
                Message = null;

                string[] lines = content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> msgLines = new List<string>();
                for (var i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.StartsWith(TreePrefix))
                    {
                        Tree = line.Substring(TreePrefix.Length);
                        continue;
                    }

                    if (line.StartsWith(ParentPrefix))
                    {
                        Parent = line.Substring(ParentPrefix.Length);
                        continue;
                    }

                    if (line.StartsWith(AuthorPrefix))
                    {
                        string authorContent = line.Substring(AuthorPrefix.Length);
                        string[] authorArray = authorContent.Split('<', '>');
                        AuthorName = authorArray[0].Trim();
                        AuthorEmail = authorArray[1].Trim();
                        string authorDate = authorArray[2].Trim();
                        string[] authorDateArray = authorDate.Split(' ');
                        if (long.TryParse(authorDateArray[0], out long unixSeconds))
                        {
                            AuthorDate = new DateTimeOffset((unixSeconds * TimeSpan.TicksPerSecond) + UnixEpochTicks, TimeSpan.Zero);
                        }

                        continue;
                    }

                    if (line.StartsWith(CommitterPrefix))
                    {
                        string committerContent = line.Substring(CommitterPrefix.Length);
                        string[] committerArray = committerContent.Split('<', '>');
                        CommitterName = committerArray[0].Trim();
                        CommitterEmail = committerArray[1].Trim();
                        string committerDate = committerArray[2].Trim();
                        string[] committerDateArray = committerDate.Split(' ');
                        if (long.TryParse(committerDateArray[0], out long unixSeconds))
                        {
                            CommitterDate = new DateTimeOffset((unixSeconds * TimeSpan.TicksPerSecond) + UnixEpochTicks, TimeSpan.Zero);
                        }

                        continue;
                    }

                    if (line.StartsWith(GpgSigPrefix))
                    {
                        string pgpLine = line.Substring(GpgSigPrefix.Length) + Environment.NewLine;
                        PgpSignature = pgpLine;
                        while (!pgpLine.Contains("END PGP SIGNATURE"))
                        {
                            i++;
                            pgpLine = lines[i];
                            PgpSignature += pgpLine + Environment.NewLine;
                        }

                        i++;
                        continue;
                    }

                    msgLines.Add(line);
                }

                Message += string.Join(Environment.NewLine, msgLines);
            }

            public static bool TryGetFromObjectFile(string filePath, out GitCommitObject commitObject)
            {
                commitObject = default;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // We skip the 2 bytes zlib header magic number.
                    fs.Seek(2, SeekOrigin.Begin);
                    using (var defStream = new DeflateStream(fs, CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[8192];
                        int readBytes = defStream.Read(buffer, 0, buffer.Length);
                        defStream.Close();

                        if (_commitByteArray.SequenceEqual(buffer.Take(_commitByteArray.Length)))
                        {
                            string strContent = Encoding.UTF8.GetString(buffer, 0, readBytes);
                            string dataContent = strContent.Substring(strContent.IndexOf('\0') + 1);
                            commitObject = new GitCommitObject(dataContent);
                            return true;
                        }
                    }
                }

                return false;
            }

            public static bool TryGetFromPackageOffset(GitPackageOffset packageOffset, out GitCommitObject commitObject)
            {
                commitObject = default;

                string packFile = Path.ChangeExtension(packageOffset.FilePath, ".pack");
                if (File.Exists(packFile))
                {
                    // packfile format explanation:
                    // https://codewords.recurse.com/issues/three/unpacking-git-packfiles#:~:text=idx%20file%20contains%20the%20index,pack%20file.&text=Objects%20in%20a%20packfile%20can,of%20storing%20the%20whole%20object.

                    using (var fs = new FileStream(packFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var br = new BigEndianBinaryReader(fs))
                    {
                        // Move to the offset of the object
                        fs.Seek(packageOffset.Offset, SeekOrigin.Begin);
                        int objectSize;
                        byte[] packData = br.ReadBytes(2);

                        if (packData[0] < 128)
                        {
                            objectSize = (int)(packData[0] & 0x0f);
                            packData = br.ReadBytes(objectSize);
                        }
                        else
                        {
                            objectSize = (((ushort)(packData[1] & 0x7f)) * 16) + ((ushort)(packData[0] & 0x0f));
                            packData = br.ReadBytes(objectSize * 100);
                        }

                        using (var ms = new MemoryStream(packData, 2, packData.Length - 2))
                        using (var defStream = new DeflateStream(ms, CompressionMode.Decompress))
                        {
                            byte[] buffer = new byte[8192];
                            int readBytes = defStream.Read(buffer, 0, buffer.Length);
                            defStream.Close();
                            string strContent = Encoding.UTF8.GetString(buffer, 0, readBytes);
                            commitObject = new GitCommitObject(strContent);
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        internal readonly struct GitPackageOffset
        {
            public readonly string FilePath;
            public readonly long Offset;

            internal GitPackageOffset(string filePath, long offset)
            {
                FilePath = filePath;
                Offset = offset;
            }

            public static bool TryGetPackageOffset(string idxFilePath, string commitSha, out GitPackageOffset packageOffset)
            {
                packageOffset = default;

                // packfile format explanation:
                // https://codewords.recurse.com/issues/three/unpacking-git-packfiles#:~:text=idx%20file%20contains%20the%20index,pack%20file.&text=Objects%20in%20a%20packfile%20can,of%20storing%20the%20whole%20object.

                string index = commitSha.Substring(0, 2);
                int folderIndex = int.Parse(index, System.Globalization.NumberStyles.HexNumber);
                int previousIndex = folderIndex > 0 ? folderIndex - 1 : folderIndex;

                using (var fs = new FileStream(idxFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BigEndianBinaryReader(fs))
                {
                    fs.Seek(8, SeekOrigin.Begin);

                    // First layer: 256 4-byte elements, with number of elements per folder
                    fs.Seek(previousIndex * 4, SeekOrigin.Current);
                    var numberOfPreviousObjects = br.ReadUInt32();
                    var numberOfObjectsInIndex = br.ReadUInt32() - numberOfPreviousObjects;
                    fs.Seek(-8, SeekOrigin.Current);

                    fs.Seek((255 - previousIndex) * 4, SeekOrigin.Current);
                    var totalNumberOfObjects = br.ReadUInt32();

                    // Second layer: 20-byte elements with the names in order
                    fs.Seek(20 * (int)numberOfPreviousObjects, SeekOrigin.Current);
                    uint? indexOfCommit = null;
                    for (uint i = 0; i < numberOfObjectsInIndex; i++)
                    {
                        var str = BitConverter.ToString(br.ReadBytes(20)).Replace("-", string.Empty);
                        if (str.Equals(commitSha, StringComparison.OrdinalIgnoreCase))
                        {
                            indexOfCommit = numberOfPreviousObjects + i;
                            fs.Seek(-20, SeekOrigin.Current);
                            break;
                        }
                    }

                    if (indexOfCommit.HasValue)
                    {
                        uint indexOfObject = indexOfCommit.Value;
                        fs.Seek(20 * (totalNumberOfObjects - indexOfObject), SeekOrigin.Current);

                        // Third layer: 4 byte CRC for each object. We skip it
                        fs.Seek(4 * totalNumberOfObjects, SeekOrigin.Current);

                        // Fourth layer: 4 byte per object of offset in pack file
                        fs.Seek(4 * indexOfObject, SeekOrigin.Current);
                        var offset = br.ReadUInt32();
                        fs.Seek(-4, SeekOrigin.Current);

                        ulong packOffset;
                        if ((offset & 0x8000000) == 0)
                        {
                            // offset is in the layer
                            packOffset = (ulong)offset;
                        }
                        else
                        {
                            // offset is not in this layer, clear first bit and look at it at the 5th layer
                            offset = offset & 0x7FFFFFFF;
                            fs.Seek(4 * (totalNumberOfObjects - indexOfObject), SeekOrigin.Current);
                            fs.Seek(8 * indexOfObject, SeekOrigin.Current);
                            packOffset = br.ReadUInt64();
                            fs.Seek(-8, SeekOrigin.Current);
                        }

                        packageOffset = new GitPackageOffset(idxFilePath, (long)packOffset);
                        return true;
                    }
                }

                return false;
            }
        }

        internal class ConfigItem
        {
            public string Type { get; set; }

            public string Name { get; set; }

            public string Url { get; set; }

            public string Remote { get; set; }

            public string Merge { get; set; }
        }

        internal class BigEndianBinaryReader : BinaryReader
        {
            public BigEndianBinaryReader(Stream stream)
                : base(stream)
            {
            }

            public override int ReadInt32()
            {
                var data = ReadBytes(4);
                Array.Reverse(data);
                return BitConverter.ToInt32(data, 0);
            }

            public override short ReadInt16()
            {
                var data = ReadBytes(2);
                Array.Reverse(data);
                return BitConverter.ToInt16(data, 0);
            }

            public override long ReadInt64()
            {
                var data = ReadBytes(8);
                Array.Reverse(data);
                return BitConverter.ToInt64(data, 0);
            }

            public override uint ReadUInt32()
            {
                var data = ReadBytes(4);
                Array.Reverse(data);
                return BitConverter.ToUInt32(data, 0);
            }
        }
    }
}