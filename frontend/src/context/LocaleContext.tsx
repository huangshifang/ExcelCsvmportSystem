import { createContext, useContext, useState, useCallback, useMemo, type ReactNode } from 'react';
import i18n from '../i18n';

type Lang = 'en' | 'zh';

interface LocaleContextType {
  lang: Lang;
  setLang: (lang: Lang) => void;
}

const LocaleContext = createContext<LocaleContextType | undefined>(undefined);

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(() => (localStorage.getItem('lang') as Lang) || 'en');

  const setLang = useCallback((newLang: Lang) => {
    localStorage.setItem('lang', newLang);
    i18n.changeLanguage(newLang);
    setLangState(newLang);
  }, []);

  const value = useMemo(() => ({ lang, setLang }), [lang, setLang]);

  return (
    <LocaleContext.Provider value={value}>
      {children}
    </LocaleContext.Provider>
  );
}

export function useLocale() {
  const ctx = useContext(LocaleContext);
  if (!ctx) throw new Error('useLocale must be used within LocaleProvider');
  return ctx;
}
