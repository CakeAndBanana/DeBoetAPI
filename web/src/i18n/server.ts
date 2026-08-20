import { cookies } from 'next/headers';
import { DEFAULT_LOCALE, LOCALE_COOKIE, isLocale, translatorFor, type Locale, type Translate } from '.';

/**
 * The reader's language, from the cookie the switcher writes.
 *
 * A cookie rather than a URL segment: the panel is a private tool behind a login, so its
 * routes never need to be shareable per language, and this keeps every existing path intact.
 */
export async function getLocale(): Promise<Locale> {
  const value = (await cookies()).get(LOCALE_COOKIE)?.value;
  return isLocale(value) ? value : DEFAULT_LOCALE;
}

/** `t` for a Server Component. */
export async function getT(): Promise<Translate> {
  return translatorFor(await getLocale());
}
