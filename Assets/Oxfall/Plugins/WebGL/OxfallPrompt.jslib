// The browser's own little text box, for typing a name in the web build (the phone keyboard does
// not open for Unity's text fields there). Returns null when the player cancels.
mergeInto(LibraryManager.library, {
  OxfallPrompt: function (titlePtr, currentPtr) {
    var answer = window.prompt(UTF8ToString(titlePtr), UTF8ToString(currentPtr));
    if (answer === null || answer === undefined) return 0;
    var size = lengthBytesUTF8(answer) + 1;
    var buffer = _malloc(size);
    stringToUTF8(answer, buffer, size);
    return buffer;
  },
});
