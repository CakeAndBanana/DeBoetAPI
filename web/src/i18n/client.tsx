'use client';

import { createContext, useContext, useMemo } from 'react';
import { DEFAULT_LOCALE, translatorFor, type Locale, type Translate } from '.';

const LocaleContext = createContext<Locale>(DEFAULT_LOCALE);

/**
 * Carries the chosen language to Client Components.
 *
 * Only the locale code crosses the boundary, not the dictionary: the dictionaries are already
 * in the client bundle, so serialising one into the RSC payload for every page would be paying
 * twice for the same strings.
 */
export function LocaleProvider({ locale, children }: { locale: Locale; children: React.ReactNode }) {
  return <LocaleContext value={locale}>{children}</LocaleContext>;
}

export function useLocale(): Locale {
  return useContext(LocaleContext);
}

/** `t` for a Client Component. */
export function useT(): Translate {
  const locale = useLocale();
  return useMemo(() => translatorFor(locale), [locale]);
}
