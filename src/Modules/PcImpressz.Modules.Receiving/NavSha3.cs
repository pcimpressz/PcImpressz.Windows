using System.Buffers.Binary;
using System.Numerics;
namespace PcImpressz.Modules.Receiving;
// FIPS 202 SHA3-512 fallback for Windows installations without a native SHA-3 provider.
public static class NavSha3
{
    private static readonly int[] Rotations=[0,1,62,28,27,36,44,6,55,20,3,10,43,25,39,41,45,15,21,8,18,2,61,56,14];
    private static readonly ulong[] Constants=[0x1UL,0x8082UL,0x800000000000808AUL,0x8000000080008000UL,0x808BUL,0x80000001UL,0x8000000080008081UL,0x8000000000008009UL,0x8AUL,0x88UL,0x80008009UL,0x8000000AUL,0x8000808BUL,0x800000000000008BUL,0x8000000000008089UL,0x8000000000008003UL,0x8000000000008002UL,0x8000000000000080UL,0x800AUL,0x800000008000000AUL,0x8000000080008081UL,0x8000000000008080UL,0x80000001UL,0x8000000080008008UL];
    public static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var state=new ulong[25];var offset=0;
        while(input.Length-offset>=72){Absorb(state,input.Slice(offset,72));offset+=72;}
        Span<byte> block=stackalloc byte[72];block.Clear();input[offset..].CopyTo(block);block[input.Length-offset]=0x06;block[71]|=0x80;Absorb(state,block);
        var result=new byte[64];for(var i=0;i<8;i++)BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(i*8,8),state[i]);return result;
    }
    private static void Absorb(ulong[] a,ReadOnlySpan<byte> block)
    {
        for(var i=0;i<9;i++)a[i]^=BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i*8,8));
        Span<ulong> c=stackalloc ulong[5];Span<ulong> b=stackalloc ulong[25];
        foreach(var rc in Constants)
        {
            for(var x=0;x<5;x++)c[x]=a[x]^a[x+5]^a[x+10]^a[x+15]^a[x+20];
            for(var x=0;x<5;x++){var d=c[(x+4)%5]^BitOperations.RotateLeft(c[(x+1)%5],1);for(var y=0;y<5;y++)a[x+5*y]^=d;}
            for(var x=0;x<5;x++)for(var y=0;y<5;y++)b[y+5*((2*x+3*y)%5)]=BitOperations.RotateLeft(a[x+5*y],Rotations[x+5*y]);
            for(var x=0;x<5;x++)for(var y=0;y<5;y++)a[x+5*y]=b[x+5*y]^((~b[(x+1)%5+5*y])&b[(x+2)%5+5*y]);
            a[0]^=rc;
        }
    }
}
