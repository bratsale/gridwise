import { useState } from 'react'
import { Admin } from './components/Admin.tsx'
import { Customer } from './components/Customer.tsx'

type Tab = 'admin' | 'customer'

export default function App() {
  const [tab, setTab] = useState<Tab>('admin')

  return (
    <div className="app">
      <header className="app-header">
        <h1>GridWise</h1>
        <p className="subtitle">Energy plans and recommendations</p>
      </header>
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'admin'} onClick={() => setTab('admin')}>
          Admin
        </button>
        <button role="tab" aria-selected={tab === 'customer'} onClick={() => setTab('customer')}>
          Customer
        </button>
      </div>
      {tab === 'admin' ? <Admin /> : <Customer />}
    </div>
  )
}
