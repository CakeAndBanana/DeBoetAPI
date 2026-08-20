import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import {
  DEFAULT_LOCALE, LOCALES, dictionaryFor, isLocale, translate, translatorFor,
  type Dictionary,
} from '.';

describe('the locale registry', () => {
  it('has English as the default, since it is the source of truth for keys', () => {
    expect(DEFAULT_LOCALE).toBe('en');
    expect(LOCALES[DEFAULT_LOCALE]).toBeDefined();
  });

  it('gives every language a label, which is what the picker shows', () => {
    for (const [code, { label }] of Object.entries(LOCALES)) {
      expect(label, `${code} needs a label`).toBeTruthy();
    }
  });
});

/**
 * The safety net for adding a language: copy en.json, translate the values, leave the keys.
 * A missing or mistyped key would otherwise only surface as an English string appearing in
 * the middle of a translated page.
 */
describe('every language covers the same keys as English', () => {
  const expected = Object.keys(en).sort();

  for (const code of Object.keys(LOCALES)) {
    it(`${code} has no missing or extra keys`, () => {
      const actual = Object.keys(dictionaryFor(code as keyof typeof LOCALES)).sort();

      expect(actual.filter((k) => !expected.includes(k)), 'extra keys').toEqual([]);
      expect(expected.filter((k) => !actual.includes(k)), 'missing keys').toEqual([]);
    });
  }

  it('leaves no value blank', () => {
    for (const code of Object.keys(LOCALES)) {
      const dictionary = dictionaryFor(code as keyof typeof LOCALES) as Record<string, string>;
      for (const [key, value] of Object.entries(dictionary)) {
        expect(value.trim(), `${code}.${key} is empty`).not.toBe('');
      }
    }
  });

  it('keeps the same placeholders in every translation', () => {
    const placeholders = (text: string) => (text.match(/\{\w+\}/g) ?? []).sort();

    for (const code of Object.keys(LOCALES)) {
      const dictionary = dictionaryFor(code as keyof typeof LOCALES) as Record<string, string>;
      for (const [key, value] of Object.entries(en as Record<string, string>)) {
        expect(placeholders(dictionary[key]), `${code}.${key}`).toEqual(placeholders(value));
      }
    }
  });
});

describe('translate', () => {
  it('returns the message for a key', () => {
    expect(translate(en, 'nav.categories')).toBe('Categories');
  });

  it('fills placeholders in', () => {
    expect(translate(en, 'categories.editTitle', { name: 'Ice Lights' }))
      .toBe('Edit Ice Lights');
  });

  it('fills every placeholder, including repeats', () => {
    expect(translate(en, 'categories.matchCount', { shown: 3, total: 40 }))
      .toBe('3 of 40 categories, with the branches they sit in.');
  });

  it('accepts numbers as well as strings', () => {
    expect(translate(en, 'categories.hidden', { count: 4 })).toBe('4 hidden');
  });

  it('falls back to English rather than rendering blank', () => {
    // A half-finished translation should degrade to a readable page, not an empty one.
    const partial = { 'nav.users': 'Gebruikers' } as unknown as Dictionary;

    expect(translate(partial, 'nav.users')).toBe('Gebruikers');
    expect(translate(partial, 'nav.categories')).toBe('Categories');
  });

  it('shows the key itself when no language has it, so the gap is visible', () => {
    const empty = {} as unknown as Dictionary;

    expect(translate(empty, 'not.a.real.key' as keyof Dictionary)).toBe('not.a.real.key');
  });
});

describe('translatorFor', () => {
  it('binds a language', () => {
    expect(translatorFor('nl')('nav.users')).toBe('Gebruikers');
    expect(translatorFor('en')('nav.users')).toBe('Users');
  });
});

describe('isLocale', () => {
  it('accepts a language that is registered', () => {
    expect(isLocale('nl')).toBe(true);
  });

  it('rejects anything else, so a hand-edited cookie cannot break a render', () => {
    expect(isLocale('xx')).toBe(false);
    expect(isLocale(undefined)).toBe(false);
    expect(isLocale('')).toBe(false);
  });
});
