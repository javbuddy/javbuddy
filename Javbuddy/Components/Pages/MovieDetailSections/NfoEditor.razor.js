// Owns the Monaco widgets and text models behind NfoEditor. BlazorMonaco's
// component Dispose() only releases its .NET object reference and never disposes the Monaco
// widget, so without this every open and edit/diff toggle left a live editor holding its models
// for the rest of the tab's life. Each step here runs synchronously in one interop call: a diff
// widget throws "TextModel got disposed before DiffEditorWidget model got reset" if its models are
// disposed while it still holds them, which is what separate .NET-side calls could not prevent.

// Monaco is only loaded once an editor is about to open, not on every page. The
// promise is shared by every NfoEditor in the tab, since ES modules are evaluated once per URL.
const monacoBase = '_content/BlazorMonaco/';
let monacoLoad = null;

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = src;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Failed to load ${src}`));
        document.body.appendChild(script);
    });
}

function loadStylesheet(href) {
    return new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;
        link.onload = resolve;
        link.onerror = () => reject(new Error(`Failed to load ${href}`));
        document.head.appendChild(link);
    });
}

async function loadMonaco() {
    const vs = `${monacoBase}lib/monaco-editor/min/vs`;
    const css = loadStylesheet(`${vs}/editor/editor.main.css`);
    // In this order, one at a time: jsInterop.js declares the global AMD config ("var require =
    // { paths }") that loader.js reads, and editor.main.js needs loader.js's define().
    await loadScript(`${monacoBase}jsInterop.js`);
    await loadScript(`${vs}/loader.js`);
    await loadScript(`${vs}/editor/editor.main.js`);
    // The script only registers the module; the "monaco" global exists once it and its dependency
    // chunks have run.
    await new Promise((resolve, reject) => window.require(['vs/editor/editor.main'], resolve, reject));
    await css;

    // A throwaway editor makes Monaco fetch the XML tokenizer and worker and apply a theme, which
    // generates the <style class="monaco-colors"> restoreMonacoHead() tracks — so it has to exist
    // before the first restoreMonacoHead() call, while that node is attached.
    const el = document.createElement('div');
    el.style.cssText = 'position:fixed;top:-9999px;left:-9999px;width:200px;height:100px;pointer-events:none;';
    document.body.appendChild(el);
    monaco.editor.create(el, { value: '<a></a>', language: 'xml', automaticLayout: false }).dispose();
    el.remove();
}

// Blazor's enhanced navigation rebuilds <head> from the newly rendered page, dropping every node
// that only ever existed in the DOM: Monaco's editor.main.css <link>, and the
// <style class="monaco-colors"> applying a theme generates (the .mtk* token colours and the
// --vscode-* variables behind selection/cursor backgrounds). Re-attaching the same nodes restores
// both, and keeps working afterwards because Monaco updates that <style> by rewriting its
// textContent. A detached node is no longer reachable from document.head, so each one is
// remembered while it's still attached.
const monacoHeadNodes = [];

function restoreMonacoHead() {
    document.head.querySelectorAll("link[href*='editor.main.css'], style.monaco-colors").forEach(node => {
        if (!monacoHeadNodes.includes(node)) monacoHeadNodes.push(node);
    });
    for (const node of monacoHeadNodes) {
        if (!node.isConnected) document.head.appendChild(node);
    }
}

// Awaited by NfoEditor before every open, not just the first: awaiting the settled load again is
// free, and every open is the moment to repair <head>, however the user reached the page.
export async function ensureMonaco() {
    monacoLoad ??= loadMonaco().catch(error => {
        monacoLoad = null;
        throw error;
    });
    await monacoLoad;
    restoreMonacoHead();
}

function holders() {
    return window.blazorMonaco?.editors ?? [];
}

function findEditor(id) {
    return holders().find(h => h.id === id)?.editor ?? null;
}

// Creates the diff's two models and hands them to the widget in the same call, so there is never
// a model that exists without an owner. Creates nothing when the widget was already disposed
// because the user left the diff view before its init finished.
export function setDiffModels(id, originalValue, modifiedValue, language, uriSuffix) {
    const editor = findEditor(id);
    if (!editor) return;

    editor.setModel({
        original: monaco.editor.createModel(originalValue, language, monaco.Uri.parse(`nfo-diff-original-${uriSuffix}`)),
        modified: monaco.editor.createModel(modifiedValue, language, monaco.Uri.parse(`nfo-diff-modified-${uriSuffix}`)),
    });
}

export function disposeEditor(id) {
    const editor = findEditor(id);
    if (!editor) return;

    const ids = [id, `${id}_original`, `${id}_modified`];
    const registry = holders();
    for (let i = registry.length - 1; i >= 0; i--) {
        if (ids.includes(registry[i].id)) registry.splice(i, 1);
    }

    const model = editor.getModel();
    const models = model && 'original' in model ? [model.original, model.modified] : [model];
    if (models.length === 2) editor.setModel(null);
    editor.dispose();
    for (const m of models) {
        if (m && !m.isDisposed()) m.dispose();
    }
}
