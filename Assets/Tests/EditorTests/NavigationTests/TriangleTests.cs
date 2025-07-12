using Navigation;
using NUnit.Framework;

namespace Tests.EditorTests.NavigationTests
{
    public class TriangleTests
    {
        [Test]
        public void AreEqual()
        {
            Assert.AreEqual(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)), 
                new Triangle(new(0, 0), new(0, 1), new(1, 1)));
            
            Assert.AreNotEqual(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 2), new(1, 1)));
        }
        
        [Test]
        public void AreFitting()
        {
            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 1), new(1, 1))));
            
            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 1), new(1, 1), new(0, 0))));
            
            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 1), new(0, 0), new(1, 1))));
            
            Assert.IsFalse(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 2), new(1, 1))));
        }
    }
}