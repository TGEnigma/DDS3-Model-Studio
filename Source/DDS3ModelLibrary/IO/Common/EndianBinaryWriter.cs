﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DDS3ModelLibrary.Primitives;

namespace DDS3ModelLibrary.IO.Common
{
    public class EndianBinaryWriter : BinaryWriter
    {
        internal class ScheduledWrite
        {
            public long Position { get; }

            public long BaseOffset { get; }

            public Func<long> Action { get; }

            public int Priority { get; }

            public object Object { get; }

            public ScheduledWrite( long position, long baseOffset, Func<long> action, int priority, object obj )
            {
                Position = position;
                BaseOffset = baseOffset;
                Action = action;
                Priority = priority;
                Object = obj;
            }
        }

        private Endianness mEndianness;
        private LinkedList<ScheduledWrite> mScheduledWrites;
        private LinkedList<long> mScheduledFileSizeWrites;
        private List<long> mOffsetPositions;
        private Dictionary<object, long> mObjectLookup;
        private Encoding mEncoding;
        private int mEncodingMinByteCount;
        private Stack<long> mBaseOffsetStack;

        public Endianness Endianness
        {
            get => mEndianness;
            set
            {
                SwapBytes = value != EndiannessHelper.SystemEndianness;
                mEndianness = value;
            }
        }

        public bool SwapBytes { get; private set; }

        public Encoding Encoding
        {
            get => mEncoding;
            set
            {
                mEncoding = value;
                mEncodingMinByteCount = mEncoding.GetByteCount( " " );
            }
        }

        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public long Length => BaseStream.Length;

        public long BaseOffset => mBaseOffsetStack.Peek();

        public IList<long> OffsetPositions => mOffsetPositions;

        public EndianBinaryWriter( Stream input, Endianness endianness )
            : base( input )
        {
            Init( Encoding.GetEncoding( 932 ), endianness );
        }

        public EndianBinaryWriter( string filepath, Endianness endianness )
            : base( File.Create( filepath ) )
        {
            Init( Encoding.GetEncoding( 932 ), endianness );
        }

        public EndianBinaryWriter( Stream input, Encoding encoding, Endianness endianness )
            : base( input, encoding )
        {
            Init( encoding, endianness );
        }

        public EndianBinaryWriter( Stream input, bool leaveOpen, Endianness endianness ) : this( input, Encoding.Default, leaveOpen, endianness ) { }

        public EndianBinaryWriter( Stream input, Encoding encoding, bool leaveOpen, Endianness endianness )
            : base( input, encoding, leaveOpen )
        {
            Init( encoding, endianness );
        }

        private void Init( Encoding encoding, Endianness endianness )
        {
            Endianness = endianness;
            Encoding = encoding;
            mScheduledWrites = new LinkedList<ScheduledWrite>();
            mScheduledFileSizeWrites = new LinkedList<long>();
            mOffsetPositions = new List<long>();
            mBaseOffsetStack = new Stack<long>();
            mBaseOffsetStack.Push( 0 );
            mObjectLookup = new Dictionary<object, long>();
        }

        public void Seek( long offset, SeekOrigin origin )
        {
            BaseStream.Seek( offset, origin );
        }

        public void SeekBegin( long offset )
        {
            BaseStream.Seek( offset, SeekOrigin.Begin );
        }

        public void SeekCurrent( long offset )
        {
            BaseStream.Seek( offset, SeekOrigin.Current );
        }

        public void SeekEnd( long offset )
        {
            BaseStream.Seek( offset, SeekOrigin.End );
        }

        public void PushBaseOffset() => mBaseOffsetStack.Push( Position );

        public void PushBaseOffset( long position ) => mBaseOffsetStack.Push( position );

        public void PopBaseOffset() => mBaseOffsetStack.Pop();

        public void Write( IEnumerable<sbyte> values )
        {
            foreach ( sbyte t in values )
                Write( t );
        }

        public void Write( IEnumerable<bool> values )
        {
            foreach ( bool t in values )
                Write( t );
        }

        public override void Write( short value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<short> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( ushort value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<ushort> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( int value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<int> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( uint value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<uint> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( long value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<long> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( ulong value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<ulong> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        private static ushort FloatToHalf( float value )
        {
            int i = Unsafe.ReinterpretCast<float, int>( value );
            int s = ( i >> 16 ) & 0x00008000;                    // sign
            int e = ( ( i >> 23 ) & 0x000000ff ) - ( 127 - 15 ); // exponent
            int f = i & 0x007fffff;                              // fraction

            // need to handle NaNs and Inf?
            if ( e <= 0 )
            {
                if ( e < -10 )
                {
                    if ( s > 0 ) // handle -0.0
                        return 0x8000;
                    return 0;
                }
                f = ( f | 0x00800000 ) >> ( 1 - e );
                return ( ushort )( s | ( f >> 13 ) );
            }

            if ( e == 0xff - ( 127 - 15 ) )
            {
                if ( f == 0 ) // Inf
                    return ( ushort )( s | 0x7c00 );
                // NAN
                f >>= 13;
                return ( ushort )( s | 0x7c00 | f | ( f == 0 ? 1 : 0 ) );
            }

            if ( e > 30 ) // Overflow
                return ( ushort )( s | 0x7c00 );
            return ( ushort )( s | ( e << 10 ) | ( f >> 13 ) );
        }

        public void WriteHalf( float value ) => Write( FloatToHalf( value ) );

        public override void Write( float value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<float> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public override void Write( decimal value )
        {
            base.Write( SwapBytes ? EndiannessHelper.Swap( value ) : value );
        }

        public void Write( IEnumerable<decimal> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public void Write( Vector2 value )
        {
            Write( value.X );
            Write( value.Y );
        }

        public void WriteVector2Half( Vector2 value )
        {
            WriteHalf( value.X );
            WriteHalf( value.Y );
        }

        public void Write( IEnumerable<Vector2> values )
        {
            foreach ( var item in values )
                Write( item );
        }

        public void Write( Vector3 value )
        {
            Write( value.X );
            Write( value.Y );
            Write( value.Z );
        }

        public void Write( IEnumerable<Vector3> values )
        {
            foreach ( var item in values )
                Write( item );
        }


        public void Write( Vector4 value )
        {
            Write( value.X );
            Write( value.Y );
            Write( value.Z );
            Write( value.W );
        }

        public void Write( IEnumerable<Vector4> values )
        {
            foreach ( var item in values )
                Write( item );
        }

        public void Write( Color color )
        {
            if ( Endianness == Endianness.Little )
            {
                Write( color.A );
                Write( color.B );
                Write( color.G );
                Write( color.R );
            }
            else
            {
                Write( color.R );
                Write( color.G );
                Write( color.B );
                Write( color.A );
            }
        }


        public void Write( IEnumerable<Color> values )
        {
            foreach ( var value in values )
                Write( value );
        }

        public void Write( string value, StringBinaryFormat format, int fixedLength = -1 )
        {
            if ( value == null )
                value = string.Empty;

            switch ( format )
            {
                case StringBinaryFormat.NullTerminated:
                    {
                        Write( Encoding.GetBytes( value ) );

                        for ( int i = 0; i < Encoding.GetByteCount( "1" ); i++ )
                            Write( ( byte )0 );
                    }
                    break;
                case StringBinaryFormat.FixedLength:
                    {
                        if ( fixedLength == -1 )
                        {
                            throw new ArgumentException( "Fixed length must be provided if format is set to fixed length", nameof( fixedLength ) );
                        }

                        var bytes = Encoding.GetBytes( value );
                        if ( bytes.Length > fixedLength )
                            Array.Resize( ref bytes, fixedLength );

                        Write( bytes );
                        fixedLength -= bytes.Length;

                        while ( fixedLength-- > 0 )
                            Write( ( byte )0 );
                    }
                    break;

                case StringBinaryFormat.PrefixedLength8:
                    {
                        Write( ( byte )value.Length );
                        Write( Encoding.GetBytes( value ) );
                    }
                    break;

                case StringBinaryFormat.PrefixedLength16:
                    {
                        Write( ( ushort )value.Length );
                        Write( Encoding.GetBytes( value ) );
                    }
                    break;

                case StringBinaryFormat.PrefixedLength32:
                    {
                        Write( ( uint )value.Length );
                        Write( Encoding.GetBytes( value ) );
                    }
                    break;

                default:
                    throw new ArgumentException( "Invalid format specified", nameof( format ) );
            }
        }

        public void WritePadding( int count )
        {
            for ( int i = 0; i < count / 8; i++ )
                Write( 0L );

            for ( int i = 0; i < count % 8; i++ )
                Write( ( byte )0 );
        }

        public void WriteAlignmentPadding( int alignment )
        {
            WritePadding( AlignmentHelper.GetAlignedDifference( Position, alignment ) );
        }

        public void ScheduleWriteOffset( Action action )
        {
            ScheduleWriteOffset( 0, null, () =>
            {
                long offset = BaseStream.Position;
                action();
                return offset;
            } );
        }

        public void ScheduleWriteOffset( Action<EndianBinaryWriter> action )
        {
            ScheduleWriteOffset( 0, null, () =>
            {
                long offset = BaseStream.Position;
                action( this );
                return offset;
            } );
        }

        public void ScheduleWriteOffset( int priority, Action action )
        {
            ScheduleWriteOffset( priority, null, () =>
            {
                long offset = BaseStream.Position;
                action();
                return offset;
            } );
        }

        public void ScheduleWriteOffsetAligned( int alignment, Action action )
        {
            ScheduleWriteOffset( 0, null, () =>
            {
                WriteAlignmentPadding( alignment );
                long offset = BaseStream.Position;
                action();
                return offset;
            } );
        }

        public void ScheduleWriteOffsetAligned( int priority, int alignment, Action action )
        {
            ScheduleWriteOffset( priority, null, () =>
            {
                WriteAlignmentPadding( alignment );
                long offset = BaseStream.Position;
                action();
                return offset;
            } );
        }

        public void ScheduleWriteFileSize()
        {
            mScheduledFileSizeWrites.AddLast( Position );
            Write( 0 );
        }

        public void WriteRelocationTable()
        {
            var basePosition = 0L;
            foreach ( var position in mOffsetPositions.OrderBy( x => x ) )
            {
                var positionDelta = position - basePosition;
                if ( positionDelta <= 0x7F )
                {
                    Write( ( byte )positionDelta );
                }
                else if ( positionDelta <= 0x7FFF )
                {
                    Write( ( byte )( 0x80 | 2 ) );
                    Write( ( ushort )positionDelta );
                }
                else
                {
                    Write( ( byte )( 0x80 | 4 ) );
                    Write( ( uint )positionDelta );
                }

                basePosition = position;
            }
        }

        private void ScheduleWriteOffset( int priority, object obj, Func<long> action )
        {
            mScheduledWrites.AddLast( new ScheduledWrite( BaseStream.Position, BaseOffset, action, priority, obj ) );
            Write( 0 );
        }

        public void PerformScheduledWrites()
        {
            DoScheduledOffsetWrites();
            DoScheduledFileSizeWrites();
        }

        internal void ScheduleWriteOffset<T>( IList<T> list, int alignment = 4, object context = null ) where T : IBinarySerializable
        {
            if ( list != null && list.Count != 0 )
            {
                ScheduleWriteOffset( 0, list, () =>
                {
                    WriteAlignmentPadding( alignment );
                    var offset = BaseStream.Position;

                    foreach ( var item in list )
                        item.Write( this, context );

                    return offset;
                } );
            }
            else
            {
                Write( 0 );
            }
        }

        internal void WriteObject<T>( T obj, object context = null ) where T : IBinarySerializable
        {
            obj.Write( this, context );
        }

        internal void WriteObjects<T>( IEnumerable<T> collection, object context = null ) where T : IBinarySerializable
        {
            foreach ( var obj in collection )
            {
                obj.Write( this, context );
            }
        }

        internal void ScheduleWriteObjectOffset( IBinarySerializable obj, int alignment = 4, object context = null )
        {
            if ( obj == null )
            {
                Write( 0 );
            }
            else
            {
                ScheduleWriteOffset( 0, obj, () =>
                {
                    WriteAlignmentPadding( alignment );
                    long current = BaseStream.Position;
                    obj.Write( this, context );
                    return current;
                } );
            }
        }

        internal void ScheduleWriteStringOffset( string obj, int alignment = 4 )
        {
            if ( obj == null )
            {
                Write( 0 );
            }
            else
            {
                ScheduleWriteOffset( 0, obj, () =>
                {
                    WriteAlignmentPadding( alignment );
                    long current = BaseStream.Position;
                    Write( obj, StringBinaryFormat.NullTerminated );
                    return current;
                } );
            }
        }

        internal void ScheduleWriteObjectOffset<T>( T obj, int alignment, Action<T> action )
        {
            if ( obj == null )
            {
                Write( 0 );
            }
            else
            {
                ScheduleWriteOffset( 0, obj, () =>
                {
                    WriteAlignmentPadding( alignment );
                    long current = BaseStream.Position;
                    action( obj );
                    return current;
                } );
            }
        }

        public int ScheduleWriteArrayOffset<T>( T[] array, int alignment, Action<T> write )
        {
            var count = 0;
            if ( array != null && array.Length != 0 )
            {
                count = array.Length;
                ScheduleWriteOffset( 0, array, () =>
                {
                    if ( alignment != 0 )
                        WriteAlignmentPadding( alignment );

                    long offset = BaseStream.Position;
                    for ( int i = 0; i < array.Length; i++ )
                        write( array[i] );

                    return offset;
                } );
            }
            else
            {
                Write( 0 );
            }

            return count;
        }
        public int ScheduleWriteListOffset<T>( List<T> list, int alignment, Action<T> write )
        {
            var count = 0;
            if ( list != null && list.Count != 0 )
            {
                count = list.Count;
                ScheduleWriteOffset( 0, list, () =>
                {
                    WriteAlignmentPadding( alignment );
                    var offset = BaseStream.Position;

                    for ( int i = 0; i < list.Count; i++ )
                        write( list[i] );

                    return offset;
                } );
            }
            else
            {
                Write( 0 );
            }

            return count;
        }

        public int ScheduleWriteListOffset<T>( List<T> list, int alignment = 4, object context = null ) where T : IBinarySerializable
        {
            var count = 0;
            if ( list != null && list.Count != 0 )
            {
                count = list.Count;
                ScheduleWriteOffset( 0, list, () =>
                {
                    WriteAlignmentPadding( alignment );
                    long offset = BaseStream.Position;
                    for ( int i = 0; i < list.Count; i++ )
                        list[i].Write( this, context );

                    return offset;
                } );
            }
            else
            {
                Write( 0 );
            }

            return count;
        }

        private void DoScheduledOffsetWrites()
        {
            int curPriority = 0;

            while ( mScheduledWrites.Count > 0 )
            {
                var anyWritesDone = false;
                var current = mScheduledWrites.First;

                while ( current != null )
                {
                    var next = current.Next;

                    if ( current.Value.Priority == curPriority )
                    {
                        DoScheduledWrite( current.Value );
                        mScheduledWrites.Remove( current );
                        anyWritesDone = true;
                    }

                    current = next;
                }

                if ( anyWritesDone )
                    ++curPriority;
                else
                    --curPriority;
            }
        }

        private void DoScheduledFileSizeWrites()
        {
            var current = mScheduledFileSizeWrites.First;
            while ( current != null )
            {
                SeekBegin( current.Value );
                Write( ( int )Length );
                current = current.Next;
            }

            mScheduledFileSizeWrites.Clear();
        }

        private void DoScheduledWrite( ScheduledWrite scheduledWrite )
        {
            long offsetPosition = scheduledWrite.Position;
            mOffsetPositions.Add( offsetPosition );

            long offset;
            if ( scheduledWrite.Object == null )
            {
                offset = scheduledWrite.Action();
            }
            else if ( !mObjectLookup.TryGetValue( scheduledWrite.Object, out offset ) ) // Try to fetch the object offset from the cache
            {
                // Object not in cache, so lets write it.

                // Write object
                offset = scheduledWrite.Action();

                // Add to lookup
                mObjectLookup[scheduledWrite.Object] = offset;
            }

            var relativeOffset = offset - scheduledWrite.BaseOffset;

            // Write offset
            long returnPos = BaseStream.Position;
            BaseStream.Seek( offsetPosition, SeekOrigin.Begin );
            Write( ( int )relativeOffset );

            // Seek back for next one
            BaseStream.Seek( returnPos, SeekOrigin.Begin );
        }

        protected override void Dispose( bool disposing )
        {
            if ( disposing )
                PerformScheduledWrites();

            base.Dispose( disposing );
        }
    }
}
