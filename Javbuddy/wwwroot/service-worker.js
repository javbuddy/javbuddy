// A fetch handler is one of Chrome's installability requirements — this app has no offline
// story, so it just passes every request straight through to the network unchanged.
self.addEventListener('fetch', function () { });
