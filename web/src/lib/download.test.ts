import { describe, expect, it } from 'vitest';
import { fileNameFrom } from './download';

const FALLBACK = 'categories.xlsx';

describe('fileNameFrom', () => {
  it('falls back when there is no header at all', () => {
    expect(fileNameFrom(null, FALLBACK)).toBe(FALLBACK);
  });

  it('falls back when the header carries no filename', () => {
    expect(fileNameFrom('attachment', FALLBACK)).toBe(FALLBACK);
  });

  it('reads a quoted filename', () => {
    expect(fileNameFrom('attachment; filename="deboet-categories-2026-08-29.xlsx"', FALLBACK))
      .toBe('deboet-categories-2026-08-29.xlsx');
  });

  it('reads an unquoted filename', () => {
    expect(fileNameFrom('attachment; filename=categories.xlsx', FALLBACK)).toBe('categories.xlsx');
  });

  // What ASP.NET actually sends: both forms, extended last. Taking the first match blindly
  // would work here by luck; taking the wrong one would percent-escape the name.
  it('prefers the extended form when both are present', () => {
    const header =
      "attachment; filename=deboet-kerst.xlsx; filename*=UTF-8''deboet-kerst%20%C3%A9%C3%A9n.xlsx";
    expect(fileNameFrom(header, FALLBACK)).toBe('deboet-kerst één.xlsx');
  });

  it('decodes a non-ascii extended filename', () => {
    expect(fileNameFrom("attachment; filename*=UTF-8''gr%C3%B6%C3%9Fe.xlsx", FALLBACK))
      .toBe('größe.xlsx');
  });

  it('falls back to the plain form when the extended one is malformed', () => {
    // A lone % is not valid percent-encoding; decodeURIComponent throws on it.
    const header = "attachment; filename=plain.xlsx; filename*=UTF-8''100%.xlsx";
    expect(fileNameFrom(header, FALLBACK)).toBe('plain.xlsx');
  });

  it('keeps only the last segment of a name with a path in it', () => {
    // A server naming the file ../../etc/passwd has no business deciding where it lands.
    expect(fileNameFrom('attachment; filename="../../etc/passwd"', FALLBACK)).toBe('passwd');
    expect(fileNameFrom('attachment; filename="C:\\\\temp\\\\sheet.xlsx"', FALLBACK))
      .toBe('sheet.xlsx');
  });

  it('falls back when the name is nothing but a path', () => {
    expect(fileNameFrom('attachment; filename="/"', FALLBACK)).toBe(FALLBACK);
  });

  it('tolerates spacing around the parameter', () => {
    expect(fileNameFrom('attachment ; filename = spaced.xlsx ', FALLBACK)).toBe('spaced.xlsx');
  });

  it('is case-insensitive about the parameter name', () => {
    expect(fileNameFrom('attachment; FileName="Shouty.xlsx"', FALLBACK)).toBe('Shouty.xlsx');
  });
});
