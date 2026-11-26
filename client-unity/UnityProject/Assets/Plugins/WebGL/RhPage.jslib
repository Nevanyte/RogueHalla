// Page facts for Bootstrap. The release document can enable the fixed split routing.
mergeInto(LibraryManager.library, {
  RhPage_GetSearch: function () {
    var s = window.location.search || "";
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },
  RhPage_UseSplitBackend: function () {
    var config = document.head.querySelector('meta[name="roguehalla-backend"]');
    return config && config.getAttribute("content") === "split" ? 1 : 0;
  }
});
