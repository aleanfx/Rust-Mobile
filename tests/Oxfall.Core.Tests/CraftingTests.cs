using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class CraftingTests
    {
        private ItemCatalog _catalog;
        private RecipeBook _book;
        private Inventory _inv;
        private CraftingQueue _queue;

        [SetUp]
        public void SetUp()
        {
            _catalog = ItemCatalog.CreateDefault();
            _book = RecipeBook.CreateDefault(_catalog);
            _inv = new Inventory(_catalog);
            _queue = new CraftingQueue(_inv);
        }

        [Test]
        public void Default_recipes_only_use_known_items()
        {
            Assert.That(_book.All.Count, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void Queuing_takes_ingredients_and_finishing_gives_the_item()
        {
            _inv.Add(ItemIds.Wood, 250);
            _inv.Add(ItemIds.Stone, 100);
            var hatchet = _book.Get("stone_hatchet");

            Assert.That(_queue.Enqueue(hatchet), Is.EqualTo(CraftingQueue.EnqueueResult.Queued));
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(50));
            Assert.That(_inv.Count(ItemIds.Stone), Is.EqualTo(0));

            _queue.Tick(hatchet.CraftSeconds - 0.5f);
            Assert.That(_inv.Count(ItemIds.StoneHatchet), Is.EqualTo(0));
            _queue.Tick(0.5f);
            Assert.That(_inv.Count(ItemIds.StoneHatchet), Is.EqualTo(1));
            Assert.That(_queue.IsBusy, Is.False);
        }

        [Test]
        public void Missing_ingredients_queue_nothing_and_take_nothing()
        {
            _inv.Add(ItemIds.Wood, 199);
            _inv.Add(ItemIds.Stone, 100);
            Assert.That(_queue.Enqueue(_book.Get("stone_pickaxe")), Is.EqualTo(CraftingQueue.EnqueueResult.MissingIngredients));
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(199));
            Assert.That(_inv.Count(ItemIds.Stone), Is.EqualTo(100));
        }

        [Test]
        public void Cancel_refunds_everything()
        {
            _inv.Add(ItemIds.Wood, 100);
            _queue.Enqueue(_book.Get("campfire"));
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(0));
            Assert.That(_queue.Cancel(0), Is.True);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(100));
        }

        [Test]
        public void One_long_tick_finishes_several_jobs_in_order()
        {
            _inv.Add(ItemIds.Wood, 60);
            var plan = _book.Get("building_plan");
            _queue.Enqueue(plan);
            _queue.Enqueue(plan);
            _queue.Enqueue(plan);
            _queue.Tick(plan.CraftSeconds * 2 + 0.1f);
            Assert.That(_inv.Count(ItemIds.BuildingPlan), Is.EqualTo(2));
            Assert.That(_queue.Jobs.Count, Is.EqualTo(1));
            Assert.That(_queue.Jobs[0].Elapsed, Is.EqualTo(0.1f).Within(1e-4));
        }

        [Test]
        public void Output_that_does_not_fit_goes_to_overflow()
        {
            var tiny = new Inventory(_catalog, Inventory.HotbarSize);
            var q = new CraftingQueue(tiny);
            tiny.Add(ItemIds.Wood, 100);
            q.Enqueue(_book.Get("campfire"));
            // Fill every slot so the finished campfire has nowhere to go.
            for (int i = 0; i < tiny.SlotCount; i++) tiny.SetSlot(i, new ItemStack(ItemIds.Rock, 1));

            string overflowItem = null;
            int overflowAmount = 0;
            q.Overflow = (id, n) => { overflowItem = id; overflowAmount = n; };
            q.Tick(100f);
            Assert.That(overflowItem, Is.EqualTo(ItemIds.Campfire));
            Assert.That(overflowAmount, Is.EqualTo(1));
        }

        [Test]
        public void The_queue_has_a_limit()
        {
            _inv.Add(ItemIds.Wood, 1000);
            var plan = _book.Get("building_plan");
            for (int i = 0; i < CraftingQueue.MaxJobs; i++)
                Assert.That(_queue.Enqueue(plan), Is.EqualTo(CraftingQueue.EnqueueResult.Queued));
            Assert.That(_queue.Enqueue(plan), Is.EqualTo(CraftingQueue.EnqueueResult.QueueFull));
        }
    }
}
