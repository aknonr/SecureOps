// Returns keyboard focus to a control by id. Used after a dialog closes: MudBlazor 6.16 does not restore focus, and the
// control that opened the dialog may have been re-rendered since. Nothing is stored; a missing or disabled control is ignored.
export function focusById(id) {
    const element = document.getElementById(id);
    if (element && !element.disabled) element.focus();
}
