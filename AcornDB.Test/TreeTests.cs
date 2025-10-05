using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcornDB.Test
{
    public class TreeTests
    {
        [Fact]
        public void Can_Stash_And_Crack_Nut()
        {
            var tree = new Tree<string>();
            tree.Stash("42", "acorn");

            var result = tree.Crack("42");

            Assert.Equal("acorn", result);
        }
    }
}
