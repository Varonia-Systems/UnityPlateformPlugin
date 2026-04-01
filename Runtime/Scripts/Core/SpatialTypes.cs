using System.Collections.Generic;
using UnityEngine;

namespace VaroniaBackOffice
{
    public class Vector3_
    {
        public float x, y, z;

        public Vector3 asVec3() => new Vector3(x, y, z);

        public Vector3_(float X, float Y, float Z) { x = X; y = Y; z = Z; }
        public Vector3_(Vector3 v)                 { x = v.x; y = v.y; z = v.z; }
        public Vector3_()                          { }
    }

    public class Vector4_
    {
        public float x, y, z, w;

        public Quaternion asQuat() => new Quaternion(x, y, z, w);

        public Vector4_()             { }
        public Vector4_(Quaternion q) { x = q.x; y = q.y; z = q.z; w = q.w; }
    }

    public enum ObstacleSize
    {
        Small  = 0,
        Medium = 1,
        Large  = 2,
    }

    public class Obstacle_
    {
        public Vector3_     Position;
        public Vector3_     Rotation;
        public ObstacleSize Size;
        public float        Scale     = 1;
        public int          SpecialId = -1;
    }

    public class Boundary_
    {
        public List<Vector3_>  Points;
        public Vector3_        BoundaryColor       = new Vector3_(1, 0, 0);
        public List<Obstacle_> Obstacles;
        public bool            AlertLimit;
        public bool            BoundaryMoreVisible;
        public bool            MainBoundary;
        public bool            Reverse;
        public float           DisplayDistance;
    }

    public class Spatial
    {
        public Vector3_        SyncPos;
        public Vector4_        SyncQuaterion;
        public List<Boundary_> Boundaries;
        public double          Multiplier;
    }
}
