//
// Copyright (c) 2006-2007 Ole Andr� Vadla Ravn�s <oleavr@gmail.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

#include "stdafx.h"
#include "Agent.h"

#ifdef _MANAGED
#pragma managed(push, off)
#endif

static HMODULE g_mod = NULL;
static Agent *g_agent = NULL;

Agent::Agent()
    : m_map(INVALID_HANDLE_VALUE),
      m_capture(NULL)
{
}

void
Agent::Initialize()
{
    InterceptPP::Initialize();

    m_map = OpenFileMapping(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, "Global\\oSpyCapture");
    m_capture = static_cast<Capture *>(MapViewOfFile(m_map, FILE_MAP_WRITE, 0, 0, sizeof(Capture)));
    if (m_capture == NULL)
        throw Error("MapViewOfFile failed");

    // Create the logger and tell Intercept++ to use it
    {
        OOWStringStream ss;
        ss << m_capture->LogPath << "\\" << GetProcessId(GetCurrentProcess()) << ".log";

        InterceptPP::SetLogger(new BinaryLogger(this, ss.str()));
    }

    m_processName = Util::Instance()->GetProcessName().c_str();

    // Load hook definitions from XML
    HookManager *mgr = HookManager::Instance();
    try
    {
        OOWStringStream ss;
        ss << m_capture->LogPath << "\\" << "config.xml";
        mgr->LoadDefinitions(ss.str());
    }
    catch (Error &e)
    {
        GetLogger()->LogError("LoadDefinitions failed: %s", e.what());
		return;
    }
    catch (...)
    {
        GetLogger()->LogError("LoadDefinitions failed: unknown error");
		return;
    }

	// Trap calls to socket functions for softwalling
	FunctionSpec *funcSpec = mgr->GetFunctionSpecById("connect");
    if (funcSpec != NULL)
	    funcSpec->SetHandler(OnSocketConnectWrapper, this);

#ifdef RESEARCH_MODE
    funcSpec = mgr->GetFunctionSpecById("WaitForSingleObject");
    if (funcSpec != NULL)
        funcSpec->SetHandler(OnWaitForSingleObject, this);

    funcSpec = mgr->GetFunctionSpecById("WaitForSingleObjectEx");
    if (funcSpec != NULL)
        funcSpec->SetHandler(OnWaitForSingleObject, this);

    funcSpec = mgr->GetFunctionSpecById("WaitForMultipleObjects");
    if (funcSpec != NULL)
        funcSpec->SetHandler(OnWaitForMultipleObjects, this);

    funcSpec = mgr->GetFunctionSpecById("WaitForMultipleObjectsEx");
    if (funcSpec != NULL)
        funcSpec->SetHandler(OnWaitForMultipleObjects, this);

    if (m_processName == "msnmsgr.exe")
    {
        funcSpec = new FunctionSpec("CP2PTransport::SendControlPacket", CALLING_CONV_STDCALL, 12);
        Function *func = new Function(funcSpec, 0x4EDD4A);
        func->Hook();

        const DWORD getQueueOffset = 0x4E4EC5;
        DWORD oldMemProtect;
        if (!VirtualProtect(reinterpret_cast<LPVOID>(getQueueOffset), sizeof(FunctionRedirectStub), PAGE_EXECUTE_WRITECOPY, &oldMemProtect))
            throw Error("VirtualProtected failed");

        FunctionRedirectStub *stub = reinterpret_cast<FunctionRedirectStub *>(getQueueOffset);
        stub->JMP_opcode = 0xE9;
        stub->JMP_offset = reinterpret_cast<DWORD>(PacketSchedulerRunAfterGetSendQueue)
            - (reinterpret_cast<DWORD>(stub) + sizeof(FunctionRedirectStub));
    }
#endif
}

void
Agent::OnSocketConnectWrapper(FunctionCall *call, void *userData, bool &shouldLog)
{
	Agent *self = static_cast<Agent *>(userData);
	self->OnSocketConnect(call);
}

void
Agent::OnSocketConnect(FunctionCall *call)
{
    if (call->GetState() != FUNCTION_CALL_ENTERING)
        return;

    char *argumentList = const_cast<char *>(call->GetArgumentsData().c_str());
    struct sockaddr_in *peerAddr = *reinterpret_cast<struct sockaddr_in **>(argumentList + sizeof(SOCKET));
    if (peerAddr->sin_family != AF_INET)
        return;

    DWORD retval, lastError;
    if (!HaveMatchingSoftwallRule("connect", call->GetReturnAddress(),
        NULL, peerAddr, retval, lastError))
    {
        return;
    }

    call->SetShouldCarryOn(false);
    call->GetCpuContextLive()->eax = retval;
    *(call->GetLastErrorLive()) = lastError;
}

#if 0
    const void *argumentList = call->GetArgumentsData().c_str();
    SOCKET s = *static_cast<const SOCKET *>(argumentList);    

    struct sockaddr_in localAddr, peerAddr;
    int sinLen;

    sinLen = sizeof(localAddr);
    getsockname(s, reinterpret_cast<struct sockaddr *>(&localAddr), &sinLen);

    sinLen = sizeof(peerAddr);
    getpeername(s, reinterpret_cast<struct sockaddr *>(&peerAddr), &sinLen);
#endif

#ifdef RESEARCH_MODE

void
Agent::OnWaitForSingleObject(FunctionCall *call, void *userData, bool &shouldLog)
{
    char *argumentList = const_cast<char *>(call->GetArgumentsData().c_str());
    DWORD timeout = *reinterpret_cast<DWORD *>(argumentList + sizeof(HANDLE));

    shouldLog = (timeout != 0 && timeout != INFINITE);
}

void
Agent::OnWaitForMultipleObjects(FunctionCall *call, void *userData, bool &shouldLog)
{
    char *argumentList = const_cast<char *>(call->GetArgumentsData().c_str());
    DWORD timeout = *reinterpret_cast<DWORD *>(argumentList + sizeof(DWORD) + sizeof(DWORD *) + sizeof(BOOL));

    shouldLog = (timeout != 0 && timeout != INFINITE);
}

typedef struct {
    DWORD field_0;
    void *m_object;
    DWORD field_8;
    DWORD field_C;
} CQueueElement;

typedef struct {
    CQueueElement **m_elements;
    DWORD field_4;
    DWORD m_numElements;
    float m_fVal1;
    float m_fVal2;
    float m_fVal3;
    DWORD field_18;
    DWORD field_1C;
    DWORD field_20;
    DWORD m_iVal;
    DWORD field_28;
    DWORD field_2C;
} CDynamicQueue;

const DWORD afterGetSendQueueNextHop = 0x4E4ECB;

static void
LogDynamicQueue(CDynamicQueue *queue)
{
    Logging::Logger *logger = GetLogger();

    Logging::Event *ev = logger->NewEvent("Debug");

    Logging::Element *queueNode = new Logging::Element("CDynamicQueue");

    Logging::Element *elementsNode = new Logging::Element("m_elements");
    queueNode->AppendChild(elementsNode);

    queueNode->AppendChild(new Logging::TextNode("field_4", queue->field_4));
    queueNode->AppendChild(new Logging::TextNode("m_numElements", queue->m_numElements));
    queueNode->AppendChild(new Logging::TextNode("m_fVal1", queue->m_fVal1));
    queueNode->AppendChild(new Logging::TextNode("m_fVal2", queue->m_fVal2));
    queueNode->AppendChild(new Logging::TextNode("m_fVal3", queue->m_fVal3));
    queueNode->AppendChild(new Logging::TextNode("field_18", queue->field_18));
    queueNode->AppendChild(new Logging::TextNode("field_1C", queue->field_1C));
    queueNode->AppendChild(new Logging::TextNode("field_20", queue->field_20));
    queueNode->AppendChild(new Logging::TextNode("m_iVal", queue->m_iVal));
    queueNode->AppendChild(new Logging::TextNode("field_28", queue->field_28));
    queueNode->AppendChild(new Logging::TextNode("field_2C", queue->field_2C));

    if (queue->field_4 != 0)
    {
        for (unsigned int i = 0; i < queue->m_numElements; i++)
        {
            CQueueElement *element = queue->m_elements[i];

            Logging::Element *elementNode = new Logging::Element("CQueueElement");
            if (element != NULL)
                elementNode->AddField("Pointer", reinterpret_cast<DWORD>(element));
            else
                elementNode->AddField("Pointer", "NULL");

            if (element != NULL)
            {
                elementNode->AppendChild(new Logging::TextNode("field_0", element->field_0));
                elementNode->AppendChild(new Logging::TextNode("m_object", element->m_object));
                elementNode->AppendChild(new Logging::TextNode("field_8", element->field_8));
                elementNode->AppendChild(new Logging::TextNode("field_C", element->field_C));
            }

            elementsNode->AppendChild(elementNode);
        }
    }

    ev->AppendChild(queueNode);

    logger->SubmitEvent(ev);
}

__declspec(naked) void
Agent::PacketSchedulerRunAfterGetSendQueue()
{
    CDynamicQueue *queue;

    __asm {
        pushad;

        push ebp;
        mov ebp, esp;
        sub esp, __LOCAL_SIZE;

        mov [queue], eax;
    }

    LogDynamicQueue(queue);

    __asm {
        leave;

        popad;

        lea ecx, [ebp-5Ch]; // Overwritten
        mov [ebp-1Ch], eax; //        code
        jmp [afterGetSendQueueNextHop];
    }
}

#endif

bool
Agent::HaveMatchingSoftwallRule(const OString &functionName,
                                void *returnAddress,
                                const sockaddr_in *localAddress,
                                const sockaddr_in *peerAddress,
                                DWORD &retval,
                                DWORD &lastError)
{
    //GetLogger()->LogDebug("Checking %s against %d rules", functionName.c_str(), m_capture->NumSoftwallRules);

    for (unsigned int i = 0; i < m_capture->NumSoftwallRules; i++)
    {
        SoftwallRule *rule = &m_capture->rules[i];

        if ((rule->Conditions & SOFTWALL_CONDITION_PROCESS_NAME) != 0)
        {
            if (m_processName != rule->ProcessName)
                continue;
        }

        if ((rule->Conditions & SOFTWALL_CONDITION_FUNCTION_NAME) != 0)
        {
            if (functionName != rule->FunctionName)
                continue;
        }

        if ((rule->Conditions & SOFTWALL_CONDITION_RETURN_ADDRESS) != 0)
        {
            if (returnAddress != rule->ReturnAddress)
                continue;
        }
        
        if ((rule->Conditions & SOFTWALL_CONDITION_LOCAL_ADDRESS) != 0)
        {
            if (localAddress == NULL ||
                memcmp(&localAddress->sin_addr, &rule->LocalAddress, sizeof(in_addr)) != 0)
            {
                continue;
            }
        }

        if ((rule->Conditions & SOFTWALL_CONDITION_LOCAL_PORT) != 0)
        {
            if (localAddress == NULL || localAddress->sin_port != rule->LocalPort)
            {
                continue;
            }
        }

        if ((rule->Conditions & SOFTWALL_CONDITION_PEER_ADDRESS) != 0)
        {
            if (peerAddress == NULL ||
                memcmp(&peerAddress->sin_addr, &rule->PeerAddress, sizeof(in_addr)) != 0)
            {
                continue;
            }
        }

        if ((rule->Conditions & SOFTWALL_CONDITION_PEER_PORT) != 0)
        {
            if (peerAddress == NULL || peerAddress->sin_port != rule->PeerPort)
            {
                continue;
            }
        }

        retval = rule->Retval;
        lastError = rule->LastError;

        //GetLogger()->LogDebug("Matched, setting retval to %d and lastError to %d", retval, lastError);
        return true;
    }

    //GetLogger()->LogDebug("No match");
    return false;
}

void
Agent::UnInitialize()
{
    // Uninitialize Intercept++, effectively unhooking everything
    InterceptPP::UnInitialize();

    // Clean up the rest
    if (m_capture != NULL)
    {
        UnmapViewOfFile(m_capture);
        m_capture = NULL;
    }

    if (m_map != NULL)
    {
        CloseHandle(m_map);
        m_map = NULL;
    }
}

LONG
Agent::GetNextLogIndex()
{
    return InterlockedIncrement(&m_capture->LogIndex);
}

LONG
Agent::AddBytesLogged(LONG n)
{
    LONG prevSize = InterlockedExchangeAdd(&m_capture->LogSize, n);

    return prevSize + n;
}

BOOL APIENTRY
DllMain(HMODULE hModule,
        DWORD  ul_reason_for_call,
        LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        //__asm int 3;

		// Just to make sure that floating point support is dynamically loaded...
		float dummy_float = 1.0f;

		// And to make sure that the compiler doesn't optimize the previous statement out.
		if (dummy_float > 0.0f)
		{
			g_mod = hModule;
            g_agent = new Agent();
            g_agent->Initialize();
		}
    }
    else if (ul_reason_for_call == DLL_PROCESS_DETACH)
    {
        if (g_agent != NULL)
        {
            g_agent->UnInitialize();
            delete g_agent;
            g_agent = NULL;
        }

        g_mod = NULL;
    }

    return TRUE;
}

#ifdef _MANAGED
#pragma managed(pop)
#endif