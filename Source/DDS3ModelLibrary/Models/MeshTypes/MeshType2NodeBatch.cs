﻿using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using DDS3ModelLibrary.IO.Common;
using DDS3ModelLibrary.PS2.VIF;

namespace DDS3ModelLibrary.Models
{
    public class MeshType2NodeBatch : IBinarySerializable
    {
        BinarySourceInfo IBinarySerializable.SourceInfo { get; set; }

        public short NodeIndex { get; set; }

        public short VertexCount => ( short )( Positions?.Length ?? 0 );

        public MeshFlags Flags { get; set; }

        public Vector4[] Positions { get; set; }

        public Vector3[] Normals { get; set; }

        public MeshBatchRenderMode RenderMode { get; set; }

        public MeshType2NodeBatch()
        {
            // 111108 SmoothShading, TexCoord, Bit5, Bit6, Bit16, Bit17, Bit18, RequiredForField, Bit22, Normal, FieldTexture, Weights
            // 155909 SmoothShading, TexCoord, Bit5, Bit6, Bit22, Normal, FieldTexture, Bit26, Weights, Bit29
            Flags = MeshFlags.SmoothShading | MeshFlags.TexCoord | MeshFlags.Bit5 | MeshFlags.Bit6 | MeshFlags.Bit22 | MeshFlags.Normal | MeshFlags.FieldTexture | MeshFlags.Bit26 | MeshFlags.Weights | MeshFlags.Bit29;

            // 215933 Mode1
            // 221821 Mode2
            RenderMode = MeshBatchRenderMode.Mode1;
        }

        void IBinarySerializable.Read( EndianBinaryReader reader, object context )
        {
            var ctx = ( IOContext )context;
            NodeIndex = ctx.NodeIndex;

            // Read header
            var headerPacket = reader.ReadObject<VifPacket>();
            headerPacket.Ensure( 0, true, true, 1, VifUnpackElementFormat.Short, 4 );
            var triangleCount = headerPacket.ShortArrays[0][0];
            var vertexCount = headerPacket.ShortArrays[0][1];
            var flags = Flags = ( MeshFlags )( ( ushort )headerPacket.ShortArrays[0][2] | ( ushort )headerPacket.ShortArrays[0][3] << 16 );

            if ( ctx.Last )
            {
                // Read triangles
                var indicesPacket = reader.ReadObject<VifPacket>();
                indicesPacket.Ensure( 1, true, true, triangleCount, VifUnpackElementFormat.Byte, 4 );
                ctx.Triangles = indicesPacket.SignedByteArrays.Select( x =>
                {
                    if ( x[3] != 0 )
                        throw new InvalidDataException();

                    return new Triangle( ( byte )x[0], ( byte )x[1], ( byte )x[2] );
                } ).ToArray();
            }

            var positionsPacket = reader.ReadObject<VifPacket>();
            positionsPacket.Ensure( !ctx.Last ? 1 : ( int? )null, true, true, vertexCount, VifUnpackElementFormat.Float, 4 );
            Positions = positionsPacket.Vector4s;

            // Read normals
            if ( flags.HasFlag( MeshFlags.Normal ) )
            {
                var normalsPacket = reader.ReadObject<VifPacket>();
                normalsPacket.Ensure( null, true, true, vertexCount, VifUnpackElementFormat.Float, 3 );
                Normals = normalsPacket.Vector3s;
            }

            if ( ctx.Last )
            {
                if ( flags.HasFlag( MeshFlags.TexCoord ) )
                {
                    var texCoordsPacket = reader.ReadObject<VifPacket>();

                    // Read texture coords
                    if ( !flags.HasFlag( MeshFlags.TexCoord2 ) )
                    {
                        texCoordsPacket.Ensure( null, true, true, vertexCount, VifUnpackElementFormat.Float, 2 );
                        ctx.TexCoords = texCoordsPacket.Vector2s;
                    }
                    else
                    {
                        texCoordsPacket.Ensure( null, true, true, vertexCount, VifUnpackElementFormat.Float, 4 );
                        ctx.TexCoords = texCoordsPacket.Vector4s.Select( x => new Vector2( x.X, x.Y ) ).ToArray();
                        ctx.TexCoords2 = texCoordsPacket.Vector4s.Select( x => new Vector2( x.Z, x.W ) ).ToArray();
                    }
                }

                if ( flags.HasFlag( MeshFlags.Color ) )
                {
                    // Read colors
                    var colorsPacket = reader.ReadObject<VifPacket>();
                    colorsPacket.Ensure( null, true, true, vertexCount, VifUnpackElementFormat.Byte, 4 );
                    ctx.Colors = colorsPacket.SignedByteArrays.Select( x => new Color( ( byte )x[0], ( byte )x[1], ( byte )x[2], ( byte )x[3] ) )
                                         .ToArray();
                }
            }

            // Read activate command
            var activateCode = reader.ReadObject<VifCode>();
            if ( activateCode.Command != VifCommand.ActMicro || ( activateCode.Immediate != 0x0C && activateCode.Immediate != 0x10 ) )
                throw new InvalidDataException();

            // Not sure if this makes any difference yet
            RenderMode = activateCode.Immediate == 0x0C ? MeshBatchRenderMode.Mode1 : MeshBatchRenderMode.Mode2;

            Debug.Assert( Flags == flags );
        }

        private void Write( IOContext context )
        {
            var vif = context.Vif;

            // Header
            vif.UnpackHeader( ( short )context.Triangles.Length, VertexCount, ( uint )Flags );

            var nextAddress = 8;

            if ( context.Last )
            {
                // Triangles
                vif.Unpack( nextAddress, context.Triangles.Select( x => new sbyte[] { ( sbyte )x.A, ( sbyte )x.B, ( sbyte )x.C, 0 } ).ToArray() );
                nextAddress = AlignmentHelper.Align( nextAddress + ( ( context.Triangles.Length * 4 ) * 2 ), 8 );
            }

            // Positions
            vif.Unpack( nextAddress, Positions );
            var effectiveVertexSize = ( int )( ( VertexCount * 12 ) / 1.5f );
            nextAddress = AlignmentHelper.Align( nextAddress + effectiveVertexSize, 8 );

            if ( Flags.HasFlag( MeshFlags.Normal ) )
            {
                // Normals
                vif.Unpack( nextAddress, Normals );
                nextAddress = AlignmentHelper.Align( nextAddress + effectiveVertexSize, 8 );
            }

            if ( context.Last )
            {
                if ( Flags.HasFlag( MeshFlags.TexCoord ) )
                {
                    if ( !Flags.HasFlag( MeshFlags.TexCoord2 ) )
                    {
                        // Texcoord 1
                        vif.Unpack( nextAddress, context.TexCoords );
                    }
                    else
                    {
                        // Texcoord 1 & 2
                        var mergedTexCoords = new Vector4[VertexCount];
                        for ( int i = 0; i < mergedTexCoords.Length; i++ )
                        {
                            mergedTexCoords[i].X = context.TexCoords[i].X;
                            mergedTexCoords[i].Y = context.TexCoords[i].Y;
                            mergedTexCoords[i].Z = context.TexCoords2[i].X;
                            mergedTexCoords[i].W = context.TexCoords2[i].Y;
                        }

                        vif.Unpack( nextAddress, mergedTexCoords );
                    }

                    nextAddress = AlignmentHelper.Align( nextAddress + ( context.TexCoords.Length * 8 ), 8 );
                }

                if ( Flags.HasFlag( MeshFlags.Color ) )
                {
                    vif.Unpack( nextAddress,
                                context.Colors.Select( x => new[] { ( sbyte )x.R, ( sbyte )x.G, ( sbyte )x.B, ( sbyte )x.A } ).ToArray() );
                }
            }

            vif.ActivateMicro( ( ushort )( RenderMode == MeshBatchRenderMode.Mode1 ? 0x0C : 0x10 ) );

        }

        void IBinarySerializable.Write( EndianBinaryWriter writer, object context )
        {
            Write( ( IOContext )context );
        }

        internal class IOContext
        {
            public short NodeIndex { get; set; }
            public bool Last { get; set; }
            public Triangle[] Triangles { get; set; }
            public Vector2[] TexCoords { get; set; }
            public Vector2[] TexCoords2 { get; set; }
            public Color[] Colors { get; set; }
            public VifCodeStreamBuilder Vif { get; }

            public IOContext( short nodeIndex, bool last )
            {
                NodeIndex = nodeIndex;
                Last = last;
            }

            public IOContext( Triangle[] triangles, Vector2[] texCoords, Vector2[] texCoords2, Color[] colors, VifCodeStreamBuilder vif )
            {
                Triangles = triangles;
                TexCoords = texCoords;
                TexCoords2 = texCoords2;
                Colors = colors;
                Vif = vif;
            }
        }
    }
}