# Cài đặt và giả lập máy X-quang trên Windows

Tài liệu dành cho Windows x64, bao gồm Windows chạy trong máy ảo. Agent nhận DICOM qua TCP và tự lưu `.dcm`; chương trình `DicomAgent.Scu` đóng vai thiết bị gửi. Không cần kết nối máy X-quang thật để thử luồng này.

## 1. Chuẩn bị

- Gói `DicomAgent-0.1.1-win-x64-Setup.exe` từ `publish/installers` của máy build.
- Tài khoản quản trị để cài service; tài khoản Windows của người vận hành để mở ứng dụng.
- Setup 0.1.1 tự tạo `%SystemDrive%\DicomSimulator` (thường là `C:\DicomSimulator`) và cài đủ SCU. Không cần build/copy SCU riêng.
- Bản self-contained không cần .NET Runtime trên máy Windows nhận gói. Máy build cần .NET SDK theo `global.json` (hiện tại 8.0.422).

Nếu đã cài thành công, chuyển đến bước 2. Các lệnh PowerShell bên dưới chạy trong Windows; riêng lệnh Bash được ghi rõ chạy trên máy build Ubuntu.

## 2. Cài đặt và kiểm tra service

1. Trên Windows, chạy `whoami` trong PowerShell để xem tài khoản người vận hành, ví dụ `WIN-PC\operator`.
2. Nếu nâng cấp, chọn **Exit desktop** trong menu tray của bản cũ trước. Chạy Setup.exe, đồng ý quyền Administrator và nhập đúng tài khoản khi bộ cài yêu cầu. Không nhập mật khẩu.
3. Đăng xuất rồi đăng nhập lại để quyền nhóm `DicomAgentOperators` có hiệu lực.
4. Mở **DICOM Desktop Agent** từ Start Menu.
5. Trong PowerShell, kiểm tra:

```powershell
Get-Service DicomAgent
```

Kết quả cần là `Running`. Nếu chưa chạy, mở PowerShell Administrator:

```powershell
Start-Service DicomAgent
```

Host nằm ở `%ProgramFiles%\DicomAgent\host`; dữ liệu nằm ở `%ProgramData%\DicomAgent`. Không chạy thêm Host bằng tay khi service đang chiếm cùng port.

## 3. Chương trình giả lập được cài sẵn

Sau khi cài Setup 0.1.1, kiểm tra trong PowerShell:

```powershell
Test-Path "$env:SystemDrive\DicomSimulator\DicomAgent.Scu.exe"
```

Mong đợi `True`. Trên Windows cài ở ổ C:, SCU nằm trong `C:\DicomSimulator`. Đây là thư mục công cụ gửi thử, khác với ứng dụng trong Program Files và ảnh nhận trong ProgramData. Với bộ cài 0.1.0 cũ, cần nâng cấp lên bộ cài mới.

Ứng dụng Desktop mở không kèm terminal. Bấm X sẽ ẩn xuống system tray, có thể nằm trong nhóm biểu tượng ẩn dưới nút **^** cạnh đồng hồ; vị trí do Windows quản lý. Nhấp biểu tượng hoặc chọn **Open DICOM Agent** để mở lại. Chọn **Exit desktop (receiver keeps running)** để thoát hẳn giao diện. Service nhận ảnh vẫn hoạt động ngay cả sau khi thoát Desktop. SCU là công cụ dòng lệnh nên vẫn chạy trong PowerShell khi kiểm thử.

Chỉ máy build cần chạy `deploy/windows/publish.ps1` để publish đủ Host, Desktop và SCU trước khi đóng gói.

## 4. Cấu hình thử nghiệm trên cùng Windows

Mở ứng dụng → **Refresh** để kết nối Host → **Settings**:

| Trường trong giao diện | Giá trị |
|---|---|
| AE Title | `DICOM_AGENT` |
| Listen IP address | `127.0.0.1` |
| Port | `11112` |
| Allowed Calling AE Titles | `XRAY_SIM` |
| Allowed source IPs | `127.0.0.1` |
| Storage directory | Để trống, dùng mặc định |
| Maximum concurrent associations | `10` |
| Maximum instance size (bytes) | `268435456` |
| Enable receiver | Bật |
| Enable file history | Bật |

Bấm **Validate → Save settings**. Mở PowerShell Administrator:

```powershell
Restart-Service DicomAgent
Get-NetTCPConnection -State Listen -LocalPort 11112
```

Trở về ứng dụng → **Refresh**, kiểm tra Dashboard có trạng thái `Running`, đúng AE và port. Thay đổi Settings chỉ áp dụng sau restart. Chế độ loopback này không cần mở firewall cho máy khác.

## 5. Giả lập thiết bị kiểm tra kết nối

Mở PowerShell bằng người dùng thường:

```powershell
Set-Location "$env:SystemDrive\DicomSimulator"
.\DicomAgent.Scu.exe echo 127.0.0.1 11112 DICOM_AGENT XRAY_SIM
```

Kết quả cần thấy: `C-ECHO: 0000 (Success)`. Đây là kiểm tra DICOM, không chỉ là kiểm tra port. Công cụ kết nối theo từng thao tác; Agent chưa có màn hình danh sách thiết bị luôn kết nối.

## 6. Giả lập chụp và gửi ảnh

Chạy trong cùng PowerShell:

```powershell
$shot = Join-Path $env:TEMP ("shot-" + [guid]::NewGuid().ToString('N') + '.dcm')
.\DicomAgent.Scu.exe generate $shot
.\DicomAgent.Scu.exe store 127.0.0.1 11112 DICOM_AGENT $shot XRAY_SIM
```

File nguồn được tạo trong TEMP vì thư mục chứa SCU chỉ cấp quyền đọc/chạy cho operator. Kết quả cần thấy: `Synthetic DX fixture created.` và `C-STORE: 0000 (Success)`.

`generate` tạo ảnh DX tổng hợp 2×2 pixel với PatientID `SYNTHETIC`, PatientName `TEST^ONLY` và UID mới. Đây là mô phỏng tạo/gửi dữ liệu, không có điều khiển phần cứng hay phát tia. Không dùng ảnh này để đánh giá chất lượng ảnh X-quang.

## 7. Kiểm tra ảnh đã lưu

1. Trong ứng dụng, bấm **Refresh → Received files**.
2. Chọn bản ghi mới → **Open file location** hoặc **Copy file path**.
3. Kiểm tra file `.dcm` nằm trong storage của Agent, khác với file nguồn trong `$env:TEMP`.

Đường dẫn mặc định:

```text
C:\ProgramData\DicomAgent\Storage\YYYY-MM-DD\StudyUID\SeriesUID\SOPUID.dcm
```

Ngày thư mục và thời gian lịch sử là UTC. Agent chưa có viewer hoặc xuất JPEG/PNG; muốn xem nội dung cần viewer DICOM riêng. Upload website hiện là chọn `.dcm` thủ công.

Thử gửi lại đúng ảnh vừa tạo:

```powershell
.\DicomAgent.Scu.exe store 127.0.0.1 11112 DICOM_AGENT $shot XRAY_SIM
```

Mong đợi vẫn Success, nhưng không tăng số file/bản ghi cho cùng ảnh. Muốn thử ảnh mới, chạy lại bước 6 để có tên file và UID mới. Đóng ứng dụng rồi gửi ảnh tiếp: service vẫn nhận; mở lại và Refresh để xem.

## 8. Thử gửi từ Ubuntu sang Windows ảo

Chỉ thực hiện sau khi loopback thành công:

1. Cấu hình mạng máy ảo để Ubuntu truy cập được IP Windows, chẳng hạn Bridged hoặc Host-only phù hợp môi trường. Với NAT, cần đường truy cập/port forwarding theo phần mềm máy ảo.
2. Dùng `ipconfig` trên Windows để lấy IP thực, ví dụ `192.168.1.10`.
3. Đổi Listen IP thành `0.0.0.0`; Allowed source IPs thành IP Ubuntu mà Windows thực sự nhìn thấy. Nếu giữ kiểm thử local, thêm `127.0.0.1`, phân cách bằng dấu phẩy.
4. Save và restart service. Cấu hình firewall theo [hướng dẫn Prod](HUONG-DAN-PROD-XQUANG.md), giới hạn nguồn là IP Ubuntu.
5. Trên Ubuntu có source/SDK, chạy tại root repo, thay IP ví dụ bằng IP Windows:

```bash
dotnet run --project tools/DicomAgent.Scu -- echo 192.168.1.10 11112 DICOM_AGENT XRAY_SIM
dotnet run --project tools/DicomAgent.Scu -- generate /tmp/windows-test-001.dcm
dotnet run --project tools/DicomAgent.Scu -- store 192.168.1.10 11112 DICOM_AGENT /tmp/windows-test-001.dcm XRAY_SIM
```

Lệnh generate không ghi đè file có sẵn; đổi tên file khi chạy lại. Nếu mạng VM có NAT, IP nguồn có thể khác IP Ubuntu dự kiến; đối chiếu với quản trị mạng.

## 9. Xử lý lỗi

| Hiện tượng | Cách kiểm tra |
|---|---|
| UI Disconnected | Service, tài khoản operator, đăng xuất/đăng nhập sau cài; không đặt `DICOM_AGENT_DATA` khác service |
| Connection refused/timeout | Service, port listening, IP; khi gửi từ ngoài kiểm tra mạng VM và firewall |
| Association rejected | Called AE phải là `DICOM_AGENT`, Calling AE `XRAY_SIM`, IP nguồn đúng allowlist; restart sau Save |
| C-STORE thất bại | Dashboard Recent errors; dung lượng, quyền storage, định dạng ảnh và giới hạn kích thước |
| File có nhưng chưa có lịch sử | Refresh, xem lỗi DB; reconciliation thử lại mỗi 30 giây; không xóa file ảnh |
| Cấu hình không đổi sau restart | Kiểm tra biến môi trường/CLI của Host có ghi đè settings không |

Hoàn tất khi C-ECHO và C-STORE đều Success, file đích tồn tại, lịch sử có một bản ghi và gửi lại không tạo bản sao. Đây là tiêu chí để người vận hành kiểm tra; tài liệu không thay thế kết quả chạy trên Windows của bạn.
