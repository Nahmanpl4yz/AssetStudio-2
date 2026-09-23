using System.IO;

namespace AssetStudio
{
    public class ResourceReader
    {
        private bool needSearch;
        private string path;
        private SerializedFile assetsFile;
        private long offset;
        private long size;
        private BinaryReader reader;

        public int Size { get => (int)size; }

        public ResourceReader(string path, SerializedFile assetsFile, long offset, long size)
        {
            needSearch = true;
            this.path = path;
            this.assetsFile = assetsFile;
            this.offset = offset;
            this.size = size;
        }

        public ResourceReader(BinaryReader reader, long offset, long size)
        {
            this.reader = reader;
            this.offset = offset;
            this.size = size;
        }

        private BinaryReader GetReader()
        {
            if (needSearch)
            {
                var resourceFileName = Path.GetFileName(path);
                if (assetsFile.assetsManager.resourceFileReaders.TryGetValue(resourceFileName, out reader))
                {
                    needSearch = false;
                    return reader;
                }
                var assetsFileDirectory = Path.GetDirectoryName(assetsFile.fullName);
                var resourceFilePath = Path.Combine(assetsFileDirectory, resourceFileName);
                if (!File.Exists(resourceFilePath))
                {
                    var findFiles = Directory.GetFiles(assetsFileDirectory, resourceFileName, SearchOption.AllDirectories);
                    if (findFiles.Length > 0)
                    {
                        resourceFilePath = findFiles[0];
                    }
                }
                if (File.Exists(resourceFilePath))
                {
                    needSearch = false;
                    reader = new BinaryReader(File.OpenRead(resourceFilePath));
                    assetsFile.assetsManager.resourceFileReaders.Add(resourceFileName, reader);
                    return reader;
                }
                throw new FileNotFoundException($"Can't find the resource file {resourceFileName}");
            }
            else
            {
                return reader;
            }
        }

        public byte[] GetData()
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;
            return binaryReader.ReadBytes((int)size);
        }

        public void GetData(byte[] buff)
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;

            // Stream.Read (and BinaryReader.Read) is only guaranteed to return at least 1 byte,
            // not to fill the requested count in one call. A single call can return early -
            // especially for larger reads - leaving the rest of buff as whatever stale bytes
            // were already in it (e.g. leftover data from an ArrayPool rental). Downstream BCn/
            // crunch decoders then treat that leftover data as real compressed bytes, producing
            // a clean run of correct blocks followed by noise once the short read's boundary is
            // hit. Loop until the full requested size has been read or the stream is exhausted.
            var total = (int)size;
            var read = 0;
            while (read < total)
            {
                var n = binaryReader.Read(buff, read, total - read);
                if (n == 0)
                {
                    throw new EndOfStreamException(
                        $"Unexpected end of stream while reading resource data: expected {total} bytes, got {read}.");
                }
                read += n;
            }
        }

        public void WriteData(string path)
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;
            using (var writer = File.OpenWrite(path))
            {
                binaryReader.BaseStream.CopyTo(writer, size);
            }
        }
    }
}
