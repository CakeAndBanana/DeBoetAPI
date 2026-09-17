import { Stack, Title } from '@mantine/core';
import { flatten } from '@/lib/categories';
import { getTree } from '@/lib/categories-api';
import { getT } from '@/i18n/server';
import { CategoryTree } from './category-tree';

export default async function CategoriesPage() {
  const t = await getT();
  const tree = await getTree();
  const options = flatten(tree);

  return (
    <Stack>
      <div>
        <Title order={2}>{t('categories.title')}</Title>
      </div>

      <CategoryTree
        nodes={tree}
        options={options}
        siteUrl={process.env.SITE_URL ?? 'https://www.deboet.nl'}
      />
    </Stack>
  );
}
