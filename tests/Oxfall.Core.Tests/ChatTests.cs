using NUnit.Framework;
using Oxfall.Core.Net;

namespace Oxfall.Core.Tests
{
    public class ChatTests
    {
        [Test]
        public void ChatIsTidiedBeforeAnyoneSeesIt()
        {
            Assert.AreEqual("hola a todos", Protocol.CleanChat("  hola \n\t a   todos  "));
            Assert.AreEqual("bcolor=redrojo/color", Protocol.CleanChat("<b><color=red>rojo</color>"), "no rich-text tags");
            Assert.IsNull(Protocol.CleanChat("   \n "), "nothing to say");
            Assert.IsNull(Protocol.CleanChat("<>"));
            Assert.IsNull(Protocol.CleanChat(null));
            Assert.AreEqual("¿base? 🙂", Protocol.CleanChat("¿base? 🙂"));
        }

        [Test]
        public void LongLinesAreCutWithoutBreakingAnEmojiOrEndingInASpace()
        {
            string longLine = Protocol.CleanChat(new string('a', 300));
            Assert.AreEqual(Protocol.MaxChatLength, longLine.Length);

            string spaced = Protocol.CleanChat(new string('a', Protocol.MaxChatLength - 1) + " b");
            Assert.AreEqual(Protocol.MaxChatLength - 1, spaced.Length, "the space and the word after it both go");
            Assert.IsFalse(spaced.EndsWith(" "));

            string emoji = Protocol.CleanChat(new string('a', Protocol.MaxChatLength - 1) + "🙂");
            Assert.AreEqual(Protocol.MaxChatLength - 1, emoji.Length, "half an emoji is not kept");
            Assert.IsFalse(char.IsHighSurrogate(emoji[emoji.Length - 1]));
        }

        [Test]
        public void ChatMessagesRoundTrip()
        {
            var w = new NetWriter();
            Messages.Chat(w, "¿alguien tiene piedra?");
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Chat, (MessageType)r.Byte());
            Assert.AreEqual("¿alguien tiene piedra?", Messages.ReadChat(r));

            Messages.ChatLine(w, 3, "Beto", "voy 🏃");
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.ChatLine, (MessageType)r.Byte());
            Messages.ReadChatLine(r, out ushort from, out string name, out string text);
            Assert.AreEqual(((ushort)3, "Beto", "voy 🏃"), (from, name, text));
        }
    }
}
