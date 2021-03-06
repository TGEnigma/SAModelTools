﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SAModelLibrary.Exceptions;
using SAModelLibrary.IO;
using SAModelLibrary.Maths;

namespace SAModelLibrary
{
    /// <summary>
    /// Represents a node in a model's hierarchy. Every model consists out of at least a root node.
    /// <remarks>Original name: NJS_OBJECT / NJS_CHNK_OBJECT</remarks>
    /// </summary>
    public class Node : ISerializableObject
    {
        private Node mParent;
        private Node mChild;
        private Node mSibling;
        private Vector3 mPosition;
        private AngleVector mRotation;
        private Vector3 mScale;
        private Matrix4x4 mLocalTransform;
        private Matrix4x4 mWorldTransform;
        private bool mLocalTransformDirty;
        private bool mWorldTransformDirty;
        private bool mPRSDirty;

        /// <inheritdoc />
        public string SourceFilePath { get; set; }

        /// <inheritdoc />
        public long SourceOffset { get; set; }

        /// <inheritdoc />
        public Endianness SourceEndianness { get; set; }

        /// <summary>
        /// Gets the evaluation flags used to determine how this node should be treated.
        /// </summary>
        public NodeFlags Flags { get; set; }

        /// <summary>
        /// Gets the geometry attached to the node.
        /// </summary>
        public IGeometry Geometry { get; set; }

        /// <summary>
        /// Gets the translation of the node.
        /// </summary>
        public Vector3 Translation
        {
            get
            {
                if ( mPRSDirty )
                    UpdatePRS();

                return mPosition;
            }
            set
            {
                if ( mPosition != value )
                {
                    mPosition            = value;
                    mLocalTransformDirty = true;
                }
            }
        }

        /// <summary>
        /// Gets the rotation of the node, in angles.
        /// </summary>
        public AngleVector Rotation
        {
            get
            {
                if ( mPRSDirty )
                    UpdatePRS();

                return mRotation;
            }
            set
            {
                if ( mRotation != value )
                {
                    mRotation = value;
                    mLocalTransformDirty = true;
                }
            }
        }

        /// <summary>
        /// Gets the scale of the node.
        /// </summary>
        public Vector3 Scale
        {
            get
            {
                if ( mPRSDirty )
                    UpdatePRS();

                return mScale;
            }
            set
            {
                if ( mScale != value )
                {
                    mScale               = value;
                    mLocalTransformDirty = true;
                }
            }
        }

        /// <summary>
        /// Gets the first child of this node.
        /// </summary>
        public Node Child
        {
            get => mChild;
            set
            {
                if ( ( mChild = value ) != null )
                    mChild.Parent = this;
            }
        }

        /// <summary>
        /// Gets the next sibling of this node.
        /// </summary>
        public Node Sibling
        {
            get => mSibling;
            set
            {
                if ( ( mSibling = value ) != null )
                    mSibling.Parent = Parent;
            }
        }

        /// <summary>
        /// Enumerates over all children of this node.
        /// </summary>
        public IEnumerable<Node> Children
        {
            get
            {
                var node = Child;
                while ( node != null )
                {
                    yield return node;
                    node = node.Sibling;
                }
            }
        }

        /// <summary>
        /// Enumerates over all siblings of this node.
        /// </summary>
        public IEnumerable<Node> Siblings
        {
            get
            {
                var node = Sibling;
                while ( node != null )
                {
                    yield return node;
                    node = node.Sibling;
                }
            }
        }

        /// <summary>
        /// Gets the parent of this node. Can be <see langword="null"/>.
        /// </summary>
        public Node Parent
        {
            get => mParent;
            set
            {
                if ( mParent != value )
                {
                    mParent              = value;
                    mWorldTransformDirty = true;

                    if ( Sibling != null )
                        Sibling.Parent = mParent;
                }
            }
        }

        /// <summary>
        /// Gets the local transform of this node.
        /// </summary>
        public Matrix4x4 Transform
        {
            get
            {
                if ( mLocalTransformDirty )
                    UpdateLocalTransform();

                return mLocalTransform;
            }
        }

        /// <summary>
        /// Gets the world transform of this node.
        /// </summary>
        public Matrix4x4 WorldTransform
        {
            get
            {
                if ( mWorldTransformDirty )
                    UpdateWorldTransform();

                return mWorldTransform;
            }
        }

        public Node()
        {
            mPosition = Vector3.Zero;
            mRotation = AngleVector.Zero;
            mScale = Vector3.One;
            mLocalTransform = Matrix4x4.Identity;
            mWorldTransform = Matrix4x4.Identity;
        }

        public Node( Vector3 translation, AngleVector rotation, Vector3 scale, Node parent )
        {
            Translation = translation;
            Rotation = rotation;
            Scale = scale;
            mParent = parent;
        }

        public Node( string filepath, GeometryFormat format )
        {
            using ( var reader = new EndianBinaryReader( filepath, Endianness.Big ) )
            {
                SourceEndianness = DetectEndianness( reader );
                Read( reader, new NodeReadContext( format ) );
            }
        }

        public Node( Stream stream, bool leaveOpen, GeometryFormat format )
        {
            using ( var reader = new EndianBinaryReader( stream, leaveOpen, Endianness.Big ) )
            {
                SourceEndianness = DetectEndianness( reader );
                Read( reader, new NodeReadContext( format ) );
            }
        }

        private void UpdateLocalTransform()
        {
            mLocalTransform = Matrix4x4.Identity;

            if ( !Flags.HasFlag( NodeFlags.IgnoreScale ) )
                mLocalTransform *= Matrix4x4.CreateScale( Scale );

            if ( !Flags.HasFlag( NodeFlags.IgnoreTranslation ) )
                mLocalTransform.Translation = Translation;


            if ( !Flags.HasFlag( NodeFlags.UseZXYRotation ) )
                mLocalTransform *= Rotation.ToRotationMatrix();
            else
                mLocalTransform *= Rotation.ToRotationMatrixZXY();

            mLocalTransformDirty = false;
        }

        private void UpdateWorldTransform()
        {
            mWorldTransform = Transform;
            if ( Parent != null && Parent != this && Parent.Parent != this )
                mWorldTransform *= Parent.WorldTransform;

            mWorldTransformDirty = false;
        }

        private void UpdatePRS()
        {
            Matrix4x4.Decompose( mLocalTransform, out var scale, out var rotation, out var translation );
            mPosition = translation;
            mRotation = AngleVector.FromQuaternion( rotation );
            mScale    = scale;
            mPRSDirty = false;
        }

        public void Save( string filepath ) => Save( filepath, SourceEndianness );

        public void Save( string filepath, Endianness endianness )
        {
            using ( var writer = new EndianBinaryWriter( filepath, endianness ) )
                Write( writer );
        }

        public void OptimizeFlags()
        {
            if ( Translation == Vector3.Zero )
                Flags |= NodeFlags.IgnoreTranslation;
            else
                Flags &= ~NodeFlags.IgnoreTranslation;

            if ( Rotation == AngleVector.Zero )
                Flags |= NodeFlags.IgnoreRotation;
            else
                Flags &= ~NodeFlags.IgnoreRotation;

            if ( Scale == Vector3.One )
                Flags |= NodeFlags.IgnoreScale;
            else
                Flags &= ~NodeFlags.IgnoreScale;

            if ( Geometry == null )
                Flags |= NodeFlags.Hide;
            else
                Flags &= ~NodeFlags.Hide;

            if ( Child == null )
                Flags |= NodeFlags.IgnoreChildren;
            else
                Flags &= ~NodeFlags.IgnoreChildren;
        }

        public static bool Validate( EndianBinaryReader reader )
        {
            var start = reader.Position;
            if ( start + 52 > reader.Length )
                return false;

            try
            {
                var flags          = (NodeFlags)reader.ReadInt32();
                var geometryOffset = reader.ReadInt32();
                var translation = reader.ReadVector3();
                var angleX = reader.ReadInt32();
                var angleY = reader.ReadInt32();
                var angleZ = reader.ReadInt32();
                var scale = reader.ReadVector3();
                var childOffset   = reader.ReadInt32();
                var siblingOffset = reader.ReadInt32();

                if ( flags == 0 && geometryOffset == 0 && translation == Vector3.Zero && angleX == 0 && angleY == 0 && angleZ == 0 &&
                     scale == Vector3.Zero && childOffset == 0 && siblingOffset == 0 )
                {
                    // All zero
                    return false;
                }

                // Check if any unspecified flags are used
                if ( ( ( int )flags & ~0x3FF ) != 0 )
                    return false;

                bool IsValidFloat( float value )
                {
                    if ( value < 0 )
                        value = -value;

                    return value == 0 || ( value >= 0.000_001f && value <= 100_000f );
                }
                bool IsValidVector3( Vector3 value ) => IsValidFloat( value.X ) && IsValidFloat( value.Y ) && IsValidFloat( value.Z );

                if ( !IsValidVector3( translation ) )
                    return false;

                if ( !IsValidVector3( scale ) )
                    return false;

                if ( scale == Vector3.Zero )
                    return false;

                if ( geometryOffset != 0 )
                {
                    if ( !reader.IsValidOffset( geometryOffset ) )
                        return false;

                    bool validGeometry = true;
                    reader.ReadAtOffset( geometryOffset, () =>
                    {
                        if ( !GeometryFormats.Basic.Geometry.Validate( reader ) && !GeometryFormats.Chunk.Geometry.Validate( reader ) &&
                             !GeometryFormats.GC.Geometry.Validate( reader ) )
                            validGeometry = false;
                    } );

                    if ( !validGeometry )
                        return false;
                }

                bool isValid = false;
                if ( childOffset != 0 )
                {
                    if ( !reader.IsValidOffset( childOffset ) )
                        return false;

                    //reader.ReadAtOffset( childOffset, () => isValid = Validate( reader ) );
                    //if ( !isValid )
                    //    return false;
                }

                if ( siblingOffset != 0 )
                {
                    if ( !reader.IsValidOffset( siblingOffset ) )
                        return false;

                    //reader.ReadAtOffset( siblingOffset, () => isValid = Validate( reader ) );
                    //if ( !isValid )
                    //    return false;
                }

                return true;
            }
            finally
            {
                reader.Position = start;
            }
        }

        private void Read( EndianBinaryReader reader, NodeReadContext context )
        {
            context.NodeCount++;

            // Fix for scanner
            if ( context.NodeCount > 1000 )
                throw new InvalidFileFormatException( "Too many nodes" );

            Parent = context.Parent;
            Flags = ( NodeFlags )reader.ReadInt32();
            Geometry = ReadGeometry( reader, context.GeometryFormat, reader.ReadInt32() );
            Translation = reader.ReadVector3();

            // Read rotation
            var angleX = reader.ReadInt32();
            var angleY = reader.ReadInt32();
            var angleZ = reader.ReadInt32();
            Rotation = new AngleVector( angleX, angleY, angleZ );

            Scale = reader.ReadVector3();
            mChild = reader.ReadObjectOffset<Node>( new NodeReadContext( this, Geometry?.Format ?? context.GeometryFormat ) { NodeCount = context.NodeCount } );
            mSibling = reader.ReadObjectOffset<Node>( context );

            // Fixes for node scanner
            {
                if ( mChild != null && mChild.Parent == null )
                    mChild.Parent = this;

                if ( mSibling != null )
                {
                    if ( Parent == null && mSibling.Parent != null )
                    {
                        Parent = mSibling.Parent;
                    }
                    else if ( Parent != null && mSibling.Parent == null )
                    {
                        mSibling.Parent = Parent;
                    }
                }

                if ( Parent != null && ( Parent == this || Parent.Parent == this ) )
                    throw new InvalidFileFormatException( "Circular dependency in parent hierarchy" );
            }
        }

        private IGeometry ReadGeometry( EndianBinaryReader reader, GeometryFormat format, int offset )
        {
            if ( offset == 0 )
                return null;

            if ( !reader.IsValidOffset( offset ) )
                throw new InvalidGeometryDataException( "Invalid geometry offset" );

            switch ( format )
            {
                case GeometryFormat.Unknown:
                    {
                        var curPosition    = reader.Position;
                        IGeometry geometry = null;

                        bool TryReadGeometry( GeometryFormat testFormat )
                        {
                            reader.Seek( offset + reader.BaseOffset, SeekOrigin.Begin );

                            switch ( testFormat )
                            {
                                case GeometryFormat.Basic:
                                case GeometryFormat.BasicDX:
                                    if ( !GeometryFormats.Basic.Geometry.Validate( reader ) )
                                        return false;
                                    break;
                                case GeometryFormat.Chunk:
                                    if ( !GeometryFormats.Chunk.Geometry.Validate( reader ) )
                                        return false;
                                    break;
                                case GeometryFormat.GC:
                                    if ( !GeometryFormats.GC.Geometry.Validate( reader ) )
                                        return false;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException( nameof( testFormat ), testFormat, null );
                            }

                            try
                            {
                                geometry = ReadGeometry( reader, testFormat, offset );
                            }
                            catch ( InvalidGeometryDataException )
                            {
                                geometry = null;
                                return false;
                            }

                            return true;
                        }

                        bool success = TryReadGeometry( GeometryFormat.Basic );
                        if ( !success ) success = TryReadGeometry( GeometryFormat.BasicDX );
                        if ( !success ) success = TryReadGeometry( GeometryFormat.Chunk );
                        if ( !success ) success = TryReadGeometry( GeometryFormat.GC );
                        if ( !success )
                            throw new InvalidGeometryDataException( "Unknown geometry format" );

                        reader.SeekBegin( curPosition );
                        return geometry;
                    }
                case GeometryFormat.Basic:
                case GeometryFormat.BasicDX:
                    return reader.ReadObjectAtOffset<GeometryFormats.Basic.Geometry>( offset, format == GeometryFormat.BasicDX );
                case GeometryFormat.Chunk:
                    return reader.ReadObjectAtOffset<GeometryFormats.Chunk.Geometry>( offset );
                case GeometryFormat.GC:
                    return reader.ReadObjectAtOffset<GeometryFormats.GC.Geometry>( offset );
                default:
                    throw new InvalidOperationException( "Invalid geometry format for reading: " + format );
            }
        }

        private void Write( EndianBinaryWriter writer )
        {
            writer.Write( ( int )Flags );
            writer.ScheduleWriteObjectOffset( Geometry );
            writer.Write( Translation );

            // Write rotation
            writer.Write( Rotation.X );
            writer.Write( Rotation.Y );
            writer.Write( Rotation.Z );

            writer.Write( Scale );
            writer.ScheduleWriteObjectOffset( Child );
            writer.ScheduleWriteObjectOffset( Sibling );
        }

        /// <summary>
        /// Performs depth first traversal over all nodes, including itself, this nodes children and its siblings.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="action"></param>
        public IEnumerable<Node> EnumerateAllNodes( bool includeSelf = true )
        {
            IEnumerable<Node> EnumerateAllNodes( Node node )
            {
                while ( node != null )
                {
                    if ( node == this && includeSelf )
                        yield return node;

                    if ( node.Child != null )
                    {
                        foreach ( var childNode in node.Child.EnumerateAllNodes( true ) )
                            yield return childNode;
                    }

                    node = node.Sibling;
                }
            }

            return EnumerateAllNodes( this );
        }

        private static Endianness DetectEndianness( EndianBinaryReader reader )
        {
            var flags = reader.ReadInt32();
            if ( ( flags & 0xFF000000 ) != 0 )
                reader.Endianness = ( Endianness )( ( int )reader.Endianness ^ 1 );

            reader.Position = 0;
            return reader.Endianness;
        }

        // ISerializableObject implementation
        void ISerializableObject.Read( EndianBinaryReader reader, object context ) =>
            Read( reader, context != null ? ( NodeReadContext ) context : new NodeReadContext( GeometryFormat.Unknown ) );

        void ISerializableObject.Write( EndianBinaryWriter writer, object context ) => Write( writer );
    }

    public class NodeReadContext
    {
        public Node Parent { get; set; }

        public GeometryFormat GeometryFormat { get; set; }

        internal int NodeCount { get; set; }

        public NodeReadContext( GeometryFormat format )
        {
            Parent = null;
            GeometryFormat = format;
        }

        public NodeReadContext( Node parent, GeometryFormat format )
        {
            Parent = parent;
            GeometryFormat = format;
        }
    }

    public class Node<TGeometry> : Node where TGeometry : IGeometry
    {
        /// <summary>
        /// Gets the geometry attached to the node.
        /// </summary>
        public new TGeometry Geometry
        {
            get => ( TGeometry )base.Geometry;
            set => base.Geometry = value;
        }

        /// <summary>
        /// Gets the first child of this node.
        /// </summary>
        public new Node<TGeometry> Child
        {
            get => ( Node<TGeometry> )base.Child;
            set => base.Child = value;
        }

        /// <summary>
        /// Gets the next sibling of this node.
        /// </summary>
        public new Node Sibling
        {
            get => ( Node<TGeometry> )base.Sibling;
            set => base.Sibling = value;
        }

        /// <summary>
        /// Enumerates over all children of this node.
        /// </summary>
        public new IEnumerable<Node<TGeometry>> Children
        {
            get => base.Children.Cast<Node<TGeometry>>();
        }

        /// <summary>
        /// Enumerates over all siblings of this node.
        /// </summary>
        public new IEnumerable<Node> Siblings
        {
            get => base.Siblings.Cast<Node<TGeometry>>();
        }

        /// <summary>
        /// Gets the parent of this node. Can be <see langword="null"/>.
        /// </summary>
        public new Node<TGeometry> Parent
        {
            get => ( Node<TGeometry> ) base.Parent;
            set => base.Parent = value;
        }
    }
}
