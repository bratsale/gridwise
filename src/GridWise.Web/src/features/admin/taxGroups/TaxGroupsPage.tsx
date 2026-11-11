import { useFormat, useT } from '../../../shared/i18n/useLanguage.ts'
import { Banner } from '../../../shared/ui/Banner.tsx'
import { TaxGroupHistory } from './components/TaxGroupHistory.tsx'
import { TaxGroupList } from './components/TaxGroupList.tsx'
import { useTaxGroupList, type TaxGroupNotice } from './hooks/useTaxGroupList.ts'
import { TaxGroupFormSection } from './TaxGroupFormSection.tsx'
import { TaxGroupRetireSection } from './TaxGroupRetireSection.tsx'

export function TaxGroupsPage() {
  const t = useT()
  const format = useFormat()
  const groups = useTaxGroupList()

  function noticeText(notice: TaxGroupNotice) {
    switch (notice.kind) {
      case 'created': return t('taxGroups.notice.created')
      case 'versionSaved': return t('taxGroups.notice.versionSaved', { version: notice.version })
      case 'retired': return t('taxGroups.notice.retired', { date: format.date(notice.date) })
    }
  }

  return (
    <section>
      <div className="toolbar">
        <button type="button" className="primary" onClick={groups.openCreate}>{t('taxGroups.new')}</button>
        <button type="button" disabled={groups.loading} onClick={() => void groups.load()}>{t('common.refresh')}</button>
      </div>

      {groups.notice && (
        // Keyed by the notice, so the next save shows a new banner with a new timer.
        <div className="notice" role="status" key={JSON.stringify(groups.notice)}>
          <Banner kind="success" onDismiss={groups.dismissNotice}>{noticeText(groups.notice)}.</Banner>
          <Banner kind="info">{t('admin.notice.delay')}</Banner>
        </div>
      )}
      {groups.listError && <Banner kind="error" message={groups.listError} />}
      {groups.rowError && <Banner kind="error" message={groups.rowError} />}

      {groups.form && (
        <TaxGroupFormSection
          key={groups.form.key} session={groups.form.session} optimistic={groups.optimistic}
          onSaved={groups.changed} onCancel={groups.closeForm}
        />
      )}
      {groups.retiring && (
        <TaxGroupRetireSection
          key={groups.retiring.code} taxGroup={groups.retiring} optimistic={groups.optimistic}
          onDone={groups.changed} onCancel={groups.closeRetire}
        />
      )}

      {groups.history && <TaxGroupHistory taxGroup={groups.history.taxGroup} versions={groups.history.versions} />}

      {/* When loading failed and nothing is known, the error above is the whole message: no "no tax groups yet". */}
      {!(groups.listError && groups.taxGroups.length === 0) && (
        <TaxGroupList
          taxGroups={groups.taxGroups}
          pending={groups.pending}
          loading={groups.loading}
          selectedCode={groups.history?.taxGroup.code ?? null}
          flashCode={groups.flash}
          onHistory={(group) => void groups.showHistory(group)}
          onEdit={(group) => void groups.edit(group)}
          onRetire={groups.openRetire}
        />
      )}
    </section>
  )
}
