using System.Collections.Generic;
using CustomNativeCollections;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditorTests.CustomNativeCollections
{
    public class NativeFixedListTests
    {
        private NativeFixedList<int> _list;

        [SetUp]
        public void Setup()
        {
            _list = new NativeFixedList<int>(10);
        }

        [TearDown]
        public void Teardown()
        {
            _list.Dispose();
        }

        [Test]
        public void Add_ShouldReturnIndex_AndStoreValue()
        {
            int index = _list.Add(42);

            _list[index].Should().Be(42);
            _list.Length.Should().Be(1);
        }

        [Test]
        public void Add_MultipleItems_ShouldPreserveOrderAndCount()
        {
            int a = _list.Add(1);
            int b = _list.Add(2);
            int c = _list.Add(3);

            _list[a].Should().Be(1);
            _list[b].Should().Be(2);
            _list[c].Should().Be(3);
            _list.Length.Should().Be(3);
        }

        [Test]
        public void RemoveAt_ShouldFreeIndex()
        {
            int index = _list.Add(99);
            _list.RemoveAt(index);
            _list.Length.Should().Be(0);
        }
        
        [Test]
        public void RemoveAt_ShouldNotChangeValuesAtIndexes()
        {
            int a = _list.Add(10);
            int b = _list.Add(11);
            int c = _list.Add(12);
            
            _list.RemoveAt(b);
            
            _list[a].Should().Be(10);
            _list[c].Should().Be(12);
        }

        [Test]
        public void Add_AfterRemove_ShouldReuseIndex()
        {
            int original = _list.Add(10);
            _list.RemoveAt(original);

            int reused = _list.Add(20);

            reused.Should().Be(original);
            _list[reused].Should().Be(20);
            _list.Length.Should().Be(1);
        }

        [Test]
        public void Count_ShouldReflectOnlyActiveItems()
        {
            int a = _list.Add(1);
            int b = _list.Add(2);
            int c = _list.Add(3);

            _list.Length.Should().Be(3);

            _list.RemoveAt(b);
            _list.Length.Should().Be(2);

            _list.Add(4); // should reuse b's index
            _list.Length.Should().Be(3);
        }
        
        [Test]
        public void Foreach_ShouldIterateOnlyActiveItems()
        {
            int i1 = _list.Add(10);
            int i2 = _list.Add(20);
            int i3 = _list.Add(30);

            // Remove the middle item
            _list.RemoveAt(i2);

            var iteratedItems = new List<int>();
            foreach (var item in _list)
            {
                iteratedItems.Add(item);
            }

            iteratedItems.Should().BeEquivalentTo(new[] { 10, 30 }, options => options.WithStrictOrdering());
        }
    }
}