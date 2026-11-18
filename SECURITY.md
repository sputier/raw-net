# Security

Eleris.RawNet reads files that come from anywhere: a damaged or hostile raw file must fail with a `RawException`, and never
crash the application, exhaust its memory or block it.

## Reporting a vulnerability

Report a problem privately, through GitHub: *Security > Report a vulnerability* on the repository
(<https://github.com/sputier/raw-net/security/advisories/new>). Please include the file that shows it, or a way to produce
it, and the version of the library.

Examples of what to report:

- a file that makes the library throw anything other than a `RawException` (an `OutOfMemoryException`, an
  `IndexOutOfRangeException`...);
- a small file that makes the library allocate much more memory than its size justifies;
- a file that keeps the library busy for a long time compared with its size;
- a way to make the library read or write outside the file or the buffers it was given.

## Supported versions

Fixes go into the latest released version.
