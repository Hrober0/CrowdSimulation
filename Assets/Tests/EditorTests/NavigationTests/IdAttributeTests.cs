using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class IdAttributeTests
    {
        [Test]
        public void Constructor_ShouldInitializeWithSingleId()
        {
            var attr = new IdAttribute(5);

            attr.Entries.Should().Be(1);
            attr.GetIds().Should().ContainSingle().Which.Should().Be(5);
        }

        [Test]
        public void GetIds_ShouldReturnAllIdsInInsertionOrder()
        {
            var attr1 = new IdAttribute(1);
            var attr2 = new IdAttribute(2);
            attr1.Merge(attr2);

            var ids = attr1.GetIds();

            ids.Should().HaveCount(2);
            ids.Should().ContainOnly(1, 2);
        }

        [Test]
        public void Merge_ShouldAppendNewId()
        {
            var attr1 = new IdAttribute(10);
            var attr2 = new IdAttribute(20);

            attr1.Merge(attr2);

            attr1.Entries.Should().Be(2);
            attr1.GetIds().Should().ContainOnly(10, 20);
        }

        [Test]
        public void Merge_ShouldNotAddDuplicateIds()
        {
            var attr1 = new IdAttribute(7);
            var attr2 = new IdAttribute(7);

            attr1.Merge(attr2);

            attr1.Entries.Should().Be(1);
            attr1.GetIds().Should().ContainSingle().Which.Should().Be(7);
        }

        [Test]
        public void Merge_MultipleIds_ShouldMergeAllUnique()
        {
            var attr1 = new IdAttribute(1);
            var attr2 = new IdAttribute(2);
            var attr3 = new IdAttribute(3);

            attr1.Merge(attr2);
            attr1.Merge(attr3);

            var ids = attr1.GetIds();
            ids.Should().HaveCount(3);
            ids.Should().ContainOnly(1, 2, 3);
        }

        [Test]
        public void Entries_ShouldMatchNumberOfUniqueIds()
        {
            var attr = new IdAttribute(1);
            attr.Merge(new IdAttribute(2));
            attr.Merge(new IdAttribute(2)); // duplicate

            attr.Entries.Should().Be(2);
        }
    
    }
}