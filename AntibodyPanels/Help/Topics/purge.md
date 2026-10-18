# Purge database

**Tools → Purge Database** removes old specimens so the database file can shrink. The application also offers this dialog at startup when the file is at or above 80 percent of the maximum size in Preferences.

The text at the top shows the current file size and the configured maximum.

## What to remove

- **Keep specimens from the last N days** deletes specimens older than that many days. The box starts at 365.
- **Delete specimens created before** a date you pick on the calendar.

The preview line states how many specimens match before you commit.

## Archive

**Create an archive of the purged data** is checked by default. Leave it checked unless you have another copy of the records. The archive holds specimen records and should be stored securely. The completion message includes the archive path. Open that file later with **Tools → Open Archive**.

**Purge** deletes the matching specimens after you confirm. Panels that are still needed by remaining specimens stay. **Cancel** closes the dialog without deleting anything.

After a purge, every tab reloads. The status bar reports how many specimens were removed and the new database size. **Help** opens this chapter.
