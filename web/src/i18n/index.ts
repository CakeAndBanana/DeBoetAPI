import en from './locales/en.json';
import nl from './locales/nl.json';

/**
 * Every language the panel ships with.
 *
 * To add one: drop `locales/<code>.json` in beside the others — copy `en.json` and translate
 * the values, leaving the keys alone — then add a line here. That is the whole procedure.
 * The registry is explicit rather than a directory scan because the bundler has to see each
 * import to include the file.
 */
export const LOCALES = {
  en: { label: 'English', dictionary: en },
  nl: { label: 'Nederlands', dictionary: nl },
} as const;

export type Locale = keyof typeof LOCALES;

/** English is the source of truth: every key exists here, so it types the rest. */
export type Dictionary = typeof en;
export type MessageKey = keyof Dictionary;

export const DEFAULT_LOCALE: Locale = 'en';
export const LOCALE_COOKIE = 'deboet_locale';

export function isLocale(value: string | undefined): value is Locale {
  return !!value && value in LOCALES;
}

export function dictionaryFor(locale: Locale): Dictionary {
  return LOCALES[locale].dictionary as Dictionary;
}

export type Vars = Record<string, string | number>;

/**
 * Looks a message up and fills its `{placeholders}`.
 *
 * A missing key falls back to English rather than rendering blank, so a half-finished
 * translation degrades to a readable page instead of an empty one. If English is missing it
 * too, the key itself is shown — visible in review, and never a silent gap.
 */
export function translate(dictionary: Dictionary, key: MessageKey, vars?: Vars): string {
  const template = dictionary[key] ?? (en as Dictionary)[key] ?? key;

  if (!vars) return template;

  return Object.entries(vars).reduce(
    (text, [name, value]) => text.replaceAll(`{${name}}`, String(value)),
    template,
  );
}

/** A bound `t`, the shape both the server and client helpers hand out. */
export type Translate = (key: MessageKey, vars?: Vars) => string;

export function translatorFor(locale: Locale): Translate {
  const dictionary = dictionaryFor(locale);
  return (key, vars) => translate(dictionary, key, vars);
}
