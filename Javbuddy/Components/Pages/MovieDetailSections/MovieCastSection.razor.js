// Closes Movie Detail's open actor-tag popover on a click outside it, or on Escape. A click on a "+ tag" pill is left
// to the section, which switches the open popover to that actor (or closes it) itself.
export function bindDismiss(host) {
    const onClick = e => {
        if (e.target.closest('.actor-pills-open, .actor-pill-add')) return;
        host.invokeMethodAsync('CloseAdding');
    };
    const onKey = e => {
        if (e.key === 'Escape') host.invokeMethodAsync('CloseAdding');
    };
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKey);
    return {
        unbind() {
            document.removeEventListener('click', onClick);
            document.removeEventListener('keydown', onKey);
        }
    };
}
