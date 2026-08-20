'use server';

import { cookies } from 'next/headers';
import { revalidatePath } from 'next/cache';
import { LOCALE_COOKIE, isLocale, type Locale } from '@/i18n';

/**
 * Remembers the chosen language.
 *
 * Not httpOnly and not a secret — it only decides which dictionary renders — but it is still
 * written server-side, because that is the only place Next lets a cookie be set outside a
 * Route Handler, and it keeps the choice available to Server Components on the very next
 * render rather than a beat later.
 */
export async function setLocale(locale: Locale): Promise<void> {
  if (!isLocale(locale)) return;

  (await cookies()).set(LOCALE_COOKIE, locale, {
    path: '/',
    maxAge: 60 * 60 * 24 * 365,
    sameSite: 'lax',
  });

  revalidatePath('/', 'layout');
}
