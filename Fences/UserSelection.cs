﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Fences.Properties;
using static System.Int32;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Fences
{
    public class UserSelection
    {
        private readonly TableCreator _tableCreator = new TableCreator();
        private readonly FileDatabase _fileDatabase = new FileDatabase();
        private Database _database;

        private Document _document;
        private Editor _editor;

        private int _guessnum = 1;
        private int _numbars = 0;

        private PromptSelectionResult _selectionResult;
        private SelectionSet _selectionSet;

        public void SelectPolyline()
        {
            _document = Application.DocumentManager.MdiActiveDocument;
            _editor = _document.Editor;
            _database = _document.Database;

            _selectionResult = _editor.GetSelection();
            _selectionSet = _selectionResult.Value;

            if (_selectionResult.Status != PromptStatus.OK) return;
            using (Transaction transaction = _document.TransactionManager.StartTransaction())
                //TODO Have to make first layer current
            {
                Settings.Default.Counter += _selectionSet.GetObjectIds().Length;
                foreach (ObjectId id in _selectionSet.GetObjectIds())
                    if (id.ObjectClass == RXObject.GetClass(typeof(Polyline)))
                    {
                        GetNumFloor();
                        Polyline pl = (Polyline) transaction.GetObject(id, OpenMode.ForRead);
                        List<Point2d> points = new List<Point2d>();

                        for (int j = 0; j < pl.NumberOfVertices; j++)
                        {
                            Point2d pt = pl.GetPoint2dAt(j);
                            points.Add(pt);
                        }
                        Fence fence = new Fence();

                        for (int i = 0; i < points.Count - 1; i++)
                        {
                            int[] segments = Divide((int) points[i].GetDistanceTo(points[i + 1]), i,
                                points.Count - 1);
                            int dist = 0;
                            Point2d[] pills = new Point2d[segments.Length - 1];

                            for (int k = 0; k < segments.Length - 1; k++)
                            {
                                dist += segments[k];
                                pills[k] = MoveDist(points[i], points[i + 1], dist);
                                DrawBar(pills[k], points[i].GetVectorTo(points[i + 1]).Angle);
                            }

                            FenceEntry entry = new FenceEntry(new LineSegment2d(points[i], points[i + 1]), pills);
                            fence.AddEntry(entry);

                            _numbars += segments.Length - 1;
                        }
                        Layer.ChangeLayer(transaction,
                            Layer.CreateLayer("КМ-РАЗМ", Color.FromColorIndex(ColorMethod.ByAci, 1),
                                LineWeight.LineWeight020), _database);

                        foreach (FenceEntry entry in fence.GetEntries())
                        foreach (LineSegment2d segment in entry.SplitByPills())
                            Dimension.Dim(segment);

                        _fileDatabase.SaveToDB(id, _guessnum, _numbars);
                        _numbars = 0;
                    }
                    else
                    {
                        MessageBox.Show(@"Используйте только полилинии");
                    }
                transaction.Commit();
            }
        }

        private void GetNumFloor()
        {
            PromptIntegerOptions options = new PromptIntegerOptions("");

            options.Message = "\nВведите количество этажей или ";
            options.AllowZero = false;
            options.AllowNegative = false;
            options.AllowNone = true;
            options.DefaultValue = _guessnum;

            PromptIntegerResult result = _document.Editor.GetInteger(options);
            if (result.Value != _guessnum)
                _guessnum = result.Value;
        }

        public int[] Divide(int lenght, int index, int n)
        {
            if (lenght < 200)
                throw new ArgumentException("Такой длины не бывает: " + lenght);
            int firstLen = 150;
            int lastLen = 150;

            if (index == 0)
                firstLen = 100;
            if (index == n - 1)
                lastLen = 100;

            if (lenght < firstLen + 190 + lastLen)
            {
                if (index == 0)
                    return new[] {firstLen, lenght - firstLen};
                return new[] {lenght - lastLen, lastLen};
            }

            int middleLen = lenght - firstLen - lastLen;
            int numSeg = middleLen % 900 == 0 ? middleLen / 900 : middleLen / 900 + 1;
            int minSegLenght = middleLen / numSeg / 10 * 10;
            int rest = middleLen - numSeg * minSegLenght;
            int[] result = new int[numSeg + 2];
            result[0] = firstLen;
            result[result.Length - 1] = lastLen;

            for (int i = 1; i < result.Length - 1; i++)
            {
                result[i] = minSegLenght;
                int curRest = Math.Min(rest, 10);
                result[i] += curRest;
                rest -= curRest;
            }

            return result;
        }

        private static Point2d MoveDist(Point2d p1, Point2d p2, double dist)
        {
            Vector2d p12 = p1.GetVectorTo(p2);
            return p1.Add(p12.GetNormal().MultiplyBy(dist));
        }

        private void DrawBar(Point2d p, double ang)
        {
            using (Transaction transaction = _document.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = transaction.GetObject(_database.BlockTableId, OpenMode.ForRead) as BlockTable;

                BlockTableRecord blockTableRecord =
                    transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as
                        BlockTableRecord;

                Layer.ChangeLayer(transaction,
                    Layer.CreateLayer("Опорная плита стойки", Color.FromColorIndex(ColorMethod.ByAci, 50),
                        LineWeight.LineWeight018), _database);

                const double w = 180;
                const double h = 120;

                Polyline bar = new Polyline();
                bar.AddVertexAt(0, p.Add(new Vector2d(w / 2, h / 2)), 0, 0, 0);
                bar.AddVertexAt(0, p.Add(new Vector2d(-w / 2, h / 2)), 0, 0, 0);
                bar.AddVertexAt(0, p.Add(new Vector2d(-w / 2, -h / 2)), 0, 0, 0);
                bar.AddVertexAt(0, p.Add(new Vector2d(w / 2, -h / 2)), 0, 0, 0);
                bar.AddVertexAt(0, p.Add(new Vector2d(w / 2, h / 2)), 0, 0, 0);

                bar.Closed = true;


                Matrix3d matrix3D = _document.Editor.CurrentUserCoordinateSystem;
                CoordinateSystem3d curUcs = matrix3D.CoordinateSystem3d;

                bar.TransformBy(Matrix3d.Rotation(ang, curUcs.Zaxis, new Point3d(p.X, p.Y, 0)));

                if (blockTableRecord != null)
                {
                    blockTableRecord.AppendEntity(bar);
                    transaction.AddNewlyCreatedDBObject(bar, true);
                }

                Layer.ChangeLayer(transaction,
                    Layer.CreateLayer("Стойки ограждений", Color.FromColorIndex(ColorMethod.ByAci, 70),
                        LineWeight.LineWeight040), _database);

                const double wr = 32;
                const double hr = 20.8;
                const double rad = 0.414213562373095;

                Polyline rack = new Polyline();
                rack.AddVertexAt(0, p.Add(new Vector2d(-wr / 2, hr / 2)), 0, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(-hr / 2, wr / 2)), rad, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(hr / 2, wr / 2)), 0, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(wr / 2, hr / 2)), rad, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(wr / 2, -hr / 2)), 0, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(hr / 2, -wr / 2)), rad, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(-hr / 2, -wr / 2)), 0, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(-wr / 2, -hr / 2)), rad, 0, 0);
                rack.AddVertexAt(0, p.Add(new Vector2d(-wr / 2, hr / 2)), 0, 0, 0);

                rack.Closed = true;

                rack.TransformBy(Matrix3d.Rotation(ang, curUcs.Zaxis, new Point3d(p.X, p.Y, 0)));

                if (blockTableRecord != null)
                {
                    blockTableRecord.AppendEntity(rack);
                    transaction.AddNewlyCreatedDBObject(rack, true);

                    DBObjectCollection acDbObjColl = rack.GetOffsetCurves(4);

                    foreach (Entity acEnt in acDbObjColl)
                    {
                        blockTableRecord.AppendEntity(acEnt);
                        transaction.AddNewlyCreatedDBObject(acEnt, true);
                    }
                }
                transaction.Commit();
            }
        }

        public void GetDataFromSelection() //TODO Finish
        {
            _document = Application.DocumentManager.MdiActiveDocument;
            _editor = _document.Editor;
            _database = _document.Database;
            _editor.WriteMessage("Выделите секцию/секции, для которых нужно создать таблицу:");

            _selectionResult = _editor.GetSelection();
            _selectionSet = _selectionResult.Value;

            if (_selectionResult.Status != PromptStatus.OK) return;

            using (Transaction transaction = _document.TransactionManager.StartTransaction())
            //TODO Have to make first layer current
            {
                Settings.Default.Counter += _selectionSet.GetObjectIds().Length;
                foreach (ObjectId id in _selectionSet.GetObjectIds())
                {
                    if (id.ObjectClass == RXObject.GetClass(typeof(Polyline)))
                    {
                        ObjectId extId = transaction.GetObject(id, OpenMode.ForRead).ExtensionDictionary;
                        DBDictionary dbExt = (DBDictionary)transaction.GetObject(extId, OpenMode.ForRead);
                        if (dbExt.Contains("CustomProp"))
                        {
                            ObjectId recId = dbExt.GetAt("TEST");
                            Xrecord readBack = (Xrecord)transaction.GetObject(recId, OpenMode.ForRead);
                            int[] dataInPl = new int[2];
                            for (int i = 0; i < 2; i++)
                            {
                                dataInPl[i] = Parse(readBack.Data.AsArray()[i].ToString());
                            }
                        }

                    }
                }
                transaction.Commit();
            }
        }
    }
}