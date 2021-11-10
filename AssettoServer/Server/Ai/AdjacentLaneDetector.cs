﻿using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SerilogTimings;

namespace AssettoServer.Server.Ai
{
    public static class AdjacentLaneDetector
    {
        public static void GetAdjacentLanesForMap(TrafficMap map, string path, float laneWidth)
        {
            if (File.Exists(path) && ParseCache(map, path)) return;
            
            DetectAdjacentLanes(map, laneWidth);
            WriteCache(map, path);
        }

        private static bool ParseCache(TrafficMap map, string cacheFilePath)
        {
            Log.Information("Parsing existing lane cache...");
            
            using var _ = Operation.Time("Parsing existing lane cache");

            using var reader = new BinaryReader(File.OpenRead(cacheFilePath));
            long fileLength = reader.BaseStream.Length;
            int pointCount = reader.ReadInt32();
            if (pointCount != map.Splines.Select(spline => spline.Points.Length).Sum())
            {
                Log.Error("Point count of lane cache differs from point count of traffic map. Cache disabled");
                return false;
            }

            try
            {
                while (true)
                {
                    int idLeft = reader.ReadInt32();
                    int idRight = reader.ReadInt32();

                    var pointLeft = map.PointsById[idLeft];
                    var pointRight = map.PointsById[idRight];

                    pointLeft.Right = pointRight;
                    pointRight.Left = pointLeft;
                }
            }
            catch (EndOfStreamException)
            {
                    
            }

            return true;
        }

        private static void WriteCache(TrafficMap map, string cacheFilePath)
        {
            Log.Information("Writing lane cache to file");
            
            using (var writer = new BinaryWriter(File.OpenWrite(cacheFilePath)))
            {
                writer.Write(map.Splines.Select(spline => spline.Points.Length).Sum());
                foreach (var spline in map.Splines)
                {
                    foreach (var point in spline.Points)
                    {
                        if (point.Right != null)
                        {
                            writer.Write(point.Id);
                            writer.Write(point.Right.Id);
                        }
                    }
                }
            }
        }

        private static Vector3 OffsetVec(Vector3 pos, float angle, float offset)
        {
            return new()
            {
                X = (float) (pos.X + Math.Cos(angle * Math.PI / 180) * offset),
                Y = pos.Y,
                Z = (float) (pos.Z - Math.Sin(angle * Math.PI / 180) * offset)
            };
        }

        private static void DetectAdjacentLanes(TrafficMap map, float laneWidth)
        {
            Log.Information("Adjacent lane detection...");
            
            using var t = Operation.Time("Adjacent lane detection");

            int i = 0;
            
            foreach (var spline in map.Splines)
            {
                Parallel.ForEach(spline.Points, point =>
                {
                    if (point.Right == null && point.Next != null)
                    {
                        float direction = (float) (Math.Atan2(point.Point.Z - point.Next.Point.Z, point.Next.Point.X - point.Point.X) * (180 / Math.PI) * -1);

                        var leftVec = OffsetVec(point.Point, -direction + 90, laneWidth);

                        var found = map.WorldToSpline(leftVec);

                        if (found.distanceSquared < 2 * 2)
                        {
                            // TODO make sure lanes are facing in the same direction.
                            // This probably breaks on right hand drive tracks
                            
                            point.Left = found.point;
                            found.point.Right = point;
                        }
                    }

                    Interlocked.Increment(ref i);

                    if (i % 1000 == 0)
                    {
                        Log.Debug("Detecting adjacent lanes, progress {0}/{1} points, {2}%", i, spline.Points.Length, Math.Round((double)i / spline.Points.Length * 100.0));
                    }
                });
            }
        }
    }
}