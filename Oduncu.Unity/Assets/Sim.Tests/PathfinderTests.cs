using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    public class PathfinderTests
    {
        [Test]
        public void StraightLineUsesDiagonals()
        {
            var map = new MapGrid(10, 10);
            var pf = new Pathfinder(map);
            var path = new List<Cell>();
            Assert.IsTrue(pf.FindPathToCell(new Cell(0, 0), new Cell(5, 5), path));
            Assert.AreEqual(5, path.Count);
            Assert.AreEqual(new Cell(5, 5), path[path.Count - 1]);
        }

        [Test]
        public void RoutesAroundWallThroughGap()
        {
            var map = new MapGrid(10, 10);
            for (int y = 0; y < 10; y++) if (y != 8) map.Occupy(new CellRect(5, y, 1), 99);
            var pf = new Pathfinder(map);
            var path = new List<Cell>();
            Assert.IsTrue(pf.FindPathToCell(new Cell(2, 2), new Cell(8, 2), path));
            bool passedGap = false;
            foreach (Cell c in path)
            {
                Assert.IsTrue(map.IsFree(c), "path crosses blocked cell " + c);
                if (c == new Cell(5, 8)) passedGap = true;
            }
            Assert.IsTrue(passedGap);
        }

        [Test]
        public void NoPathWhenFullyWalled()
        {
            var map = new MapGrid(10, 10);
            for (int y = 0; y < 10; y++) map.Occupy(new CellRect(5, y, 1), 99);
            var pf = new Pathfinder(map);
            var path = new List<Cell>();
            Assert.IsFalse(pf.FindPathToCell(new Cell(2, 2), new Cell(8, 2), path));
        }

        [Test]
        public void NoCornerCutting()
        {
            var map = new MapGrid(5, 5);
            map.Occupy(new CellRect(1, 0, 1), 1);
            map.Occupy(new CellRect(0, 1, 1), 2);
            var pf = new Pathfinder(map);
            var path = new List<Cell>();
            // (0,0) is boxed in diagonally; only route is none.
            Assert.IsFalse(pf.FindPathToCell(new Cell(0, 0), new Cell(3, 3), path));
        }

        [Test]
        public void BlockedGoalPathsToAdjacentCell()
        {
            var map = new MapGrid(10, 10);
            map.Occupy(new CellRect(6, 6, 3), 7);
            var pf = new Pathfinder(map);
            var path = new List<Cell>();
            Assert.IsTrue(pf.FindPathAdjacentToRect(new Cell(0, 0), new CellRect(6, 6, 3), path));
            Cell last = path[path.Count - 1];
            Assert.AreEqual(1, new CellRect(6, 6, 3).DistanceTo(last));
        }
    }
}
