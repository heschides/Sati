function Test-SatiEmbeddedLocalDbPrerequisite {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallerPath, [Parameter(Mandatory)][string]$WorkingRoot)

    # Read format 6 of the .NET bundle manifest and its managed resource. No SDK is required,
    # and neither the bootstrap entry point nor the embedded MSI is executed.
    if (-not ('SatiInstallerBundleReader' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
public static class SatiInstallerBundleReader {
    private static string Text(BinaryReader r, int limit) {
        int length=0, shift=0;
        for (int i=0;i<5;i++) {
            byte b=r.ReadByte(); length |= (b & 127) << shift;
            if ((b & 128)==0) {
                if (length<0 || length>limit) throw new InvalidDataException("Bundle string exceeds its bound.");
                byte[] bytes=r.ReadBytes(length);
                if (bytes.Length!=length) throw new EndOfStreamException();
                return Encoding.UTF8.GetString(bytes);
            }
            shift+=7;
        }
        throw new InvalidDataException("Invalid bundle string length.");
    }
    public static void Extract(string path, string destination) {
        byte[] signature={0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae};
        byte[] managed=null;
        using (var file=File.OpenRead(path)) using (var r=new BinaryReader(file)) {
            byte[] host=new byte[65536+40];
            long header=0;
            int carry=0, readHost;
            while((readHost=file.Read(host,carry,65536))>0) {
                int available=carry+readHost;
                for (int i=8;i<=available-signature.Length;i++) {
                    if(host[i]!=signature[0]) continue;
                    bool match=true;
                    for(int j=1;j<signature.Length;j++) if(host[i+j]!=signature[j]) { match=false; break; }
                    if(match) { header=BitConverter.ToInt64(host,i-8); break; }
                }
                if(header!=0) break;
                carry=Math.Min(40,available);
                Array.Copy(host,available-carry,host,0,carry);
            }
            if(header<=0 || header>=file.Length-12) throw new InvalidDataException("Bundle manifest unavailable.");
            file.Position=header;
            if(r.ReadUInt32()!=6 || r.ReadUInt32()!=0) throw new InvalidDataException("Unsupported bundle format.");
            int count=r.ReadInt32();
            if(count<1 || count>4096) throw new InvalidDataException("Invalid bundle entry count.");
            Text(r,128);
            for(int i=0;i<5;i++) r.ReadInt64();
            long start=0,size=0,compressed=0;
            bool found=false;
            for(int i=0;i<count;i++) {
                long s=r.ReadInt64(), n=r.ReadInt64(), z=r.ReadInt64(); r.ReadByte();
                string name=Text(r,16384);
                if(name=="SatiLocalSetup.dll") {
                    if(found) throw new InvalidDataException("Duplicate managed bootstrap entry.");
                    start=s; size=n; compressed=z; found=true;
                }
            }
            long stored=compressed>0?compressed:size;
            if(!found || start<0 || size<1 || size>500L*1024*1024 || compressed<0 || stored<1 || stored>500L*1024*1024 || stored>header || start>header-stored)
                throw new InvalidDataException("Managed bootstrap bounds invalid.");
            file.Position=start;
            byte[] bytes=r.ReadBytes((int)stored);
            if(bytes.Length!=stored) throw new EndOfStreamException();
            if(compressed>0) {
                using(var input=new MemoryStream(bytes)) using(var inflater=new DeflateStream(input,CompressionMode.Decompress)) using(var output=new MemoryStream()) {
                    byte[] buffer=new byte[65536]; int read;
                    while((read=inflater.Read(buffer,0,buffer.Length))>0) {
                        if(output.Length+read>size) throw new InvalidDataException("Decompressed bundle exceeds declared size.");
                        output.Write(buffer,0,read);
                    }
                    managed=output.ToArray();
                }
            } else managed=bytes;
            if(managed.LongLength!=size) throw new InvalidDataException("Managed bundle size differs.");
        }
        // Reflection reads resources only; it never calls the app's entry point.
        Assembly assembly=Assembly.Load(managed);
        using(var resource=assembly.GetManifestResourceStream("SqlLocalDB.msi")) {
            if(resource==null) throw new InvalidDataException("Embedded LocalDB MSI missing.");
            using(var output=File.Open(destination,FileMode.CreateNew,FileAccess.Write)) resource.CopyTo(output);
        }
    }
}
'@
    }
    $root = [IO.Path]::GetFullPath($WorkingRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container) -or $root -eq [IO.Path]::GetPathRoot($root)) { throw 'A non-root inspection directory is required.' }
    $inspection = Join-Path $root ('prerequisite-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($inspection)
    $msi = Join-Path $inspection 'SqlLocalDB.msi'
    try {
        [SatiInstallerBundleReader]::Extract([IO.Path]::GetFullPath($InstallerPath), $msi)
        $signature = Get-AuthenticodeSignature -LiteralPath $msi
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') { throw 'Embedded LocalDB prerequisite lacks a valid Microsoft signature.' }
        [pscustomobject]@{ Status=[string]$signature.Status; Signer=$signature.SignerCertificate.Subject; Sha256=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant() }
    } finally {
        $resolved=[IO.Path]::GetFullPath($inspection)
        if ([IO.Path]::GetDirectoryName($resolved) -ine $root -or [IO.Path]::GetFileName($resolved) -cnotmatch '^prerequisite-[0-9a-f]{32}$') { throw 'Unexpected prerequisite inspection path.' }
        if (Test-Path -LiteralPath $msi) { Remove-Item -LiteralPath $msi -Force }
        Remove-Item -LiteralPath $resolved
    }
}
