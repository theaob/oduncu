using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    public class FPTests
    {
        [Test]
        public void IntegerArithmeticIsExact()
        {
            FP a = 6, b = 7;
            Assert.AreEqual(42, (a * b).FloorToInt());
            Assert.AreEqual(13, (a + b).FloorToInt());
            Assert.AreEqual(-1, (a - b).FloorToInt());
            Assert.AreEqual(FP.FromInt(3), FP.FromInt(21) / FP.FromInt(7));
        }

        [Test]
        public void FractionsMultiplyWithBoundedError()
        {
            FP x = FP.Ratio(1, 3);
            FP y = FP.Ratio(3, 1);
            FP product = x * y;
            Assert.IsTrue(FP.Abs(product - FP.One) < FP.Ratio(1, 10000), "1/3 * 3 should be close to 1, got " + product);
        }

        [Test]
        public void NegativeMultiplicationMatchesSign()
        {
            FP a = FP.Ratio(-5, 2);
            FP b = FP.Ratio(3, 2);
            FP p = a * b;
            Assert.AreEqual(FP.Ratio(-15, 4).Raw, p.Raw, 2);
        }

        [Test]
        public void LargeProductsDoNotOverflow()
        {
            FP a = FP.FromInt(100000);
            FP b = FP.FromInt(20000);
            Assert.AreEqual(2000000000L, (a * b).FloorToInt());
        }

        [Test]
        public void SqrtOfPerfectSquaresIsExact()
        {
            Assert.AreEqual(FP.FromInt(12), FP.Sqrt(FP.FromInt(144)));
            Assert.AreEqual(FP.Zero, FP.Sqrt(FP.Zero));
            Assert.AreEqual(FP.Ratio(3, 2), FP.Sqrt(FP.Ratio(9, 4)));
        }

        [Test]
        public void SqrtOfTwoIsClose()
        {
            FP r = FP.Sqrt(FP.FromInt(2));
            Assert.IsTrue(FP.Abs(r * r - FP.FromInt(2)) < FP.Ratio(1, 1000));
        }

        [Test]
        public void RoundingHelpers()
        {
            FP v = FP.Ratio(7, 2); // 3.5
            Assert.AreEqual(3, v.FloorToInt());
            Assert.AreEqual(4, v.CeilToInt());
            Assert.AreEqual(4, v.RoundToInt());
            Assert.AreEqual(-4, (-v).FloorToInt());
        }

        [Test]
        public void NormalizedVectorHasUnitLength()
        {
            var v = new FPVector2(FP.FromInt(3), FP.FromInt(-4));
            FP len = v.Normalized.Magnitude;
            Assert.IsTrue(FP.Abs(len - FP.One) < FP.Ratio(1, 1000), "length " + len);
            Assert.AreEqual(FPVector2.Zero, FPVector2.Zero.Normalized);
        }

        [Test]
        public void RandomIsRepeatable()
        {
            var a = new DeterministicRandom(123);
            var b = new DeterministicRandom(123);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.Next(1000), b.Next(1000));
        }
    }
}
