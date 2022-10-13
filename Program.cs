using Modbus.Device;
using Modbus.Serial;
using Newtonsoft.Json;
using RasberryApp;
using System.Device.Gpio;
using System.IO.Ports;
using System.Net;
using System.Net.Http.Headers;

namespace RasberryApp
{

    public class Program
    {


        static string UrlLocalApi = "http://damian16-001-site1.htempurl.com/GetRun";
        static SerialPort port = new SerialPort();
        public static bool ActEstado { get; set; }
        public static ushort velocidad { get; set; }

        public static bool PararRotor { get; set; }

        public static SerialPortAdapter adapter = new SerialPortAdapter(port);

        public static IModbusSerialMaster master = ModbusSerialMaster.CreateRtu(adapter);

        public static int pin = 17;
        public static GpioController controller = new GpioController();
        public static readonly HttpClient clienthttp = new HttpClient();

        static async Task Main(string[] args)
        {
            try
            {
                controller.OpenPin(pin, PinMode.Output);
                port.PortName = "/dev/ttyUSB0";
                //port.PortName = "COM11";
                port.Parity = Parity.None;
                port.BaudRate = 9600;
                port.DataBits = 8;
                port.StopBits = StopBits.One;
                port.ReadTimeout = 200;
                port.WriteTimeout = 200;
                if (port.IsOpen)
                {
                    port.Close();
                    port.Dispose();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("No hay puerto disponible");
            }           



            Thread CnApi = new Thread(new ThreadStart(ConsultApiAsync));
            CnApi.Start();


            Thread RMotor = new Thread(new ThreadStart(RunMotor));
            RMotor.Start();

            Thread RotorBloqueado = new Thread(new ThreadStart(StopMotor));
            RotorBloqueado.Start();

            Thread accion = new Thread(new ThreadStart(UpdateAccion));
            accion.Start();

            while (true)
            {
                //ConsultApiAsync().GetAwaiter().GetResult();
                RunAsync().GetAwaiter().GetResult();
            }
        }
        public static void ConsultApiAsync()
        {
            while (true)
            {

                using (var client = new HttpClient())
                {
                    try
                    {
                        var response = client.GetAsync(UrlLocalApi).Result;
                        var responcevelocidad = client.GetAsync("http://damian16-001-site1.htempurl.com/GetVelocidad").Result;
                        var responceBloquearRotor = client.GetAsync("http://damian16-001-site1.htempurl.com/GetAccionSource").Result;
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = response.Content;                            
                            string responseString = responseContent.ReadAsStringAsync().Result;
                            var objResponse = JsonConvert.DeserializeObject<RespuestaRun>(responseString).Codigo;
                            if (objResponse != ActEstado)
                            {
                                switch (objResponse)
                                {
                                    case true:
                                        Console.WriteLine(responseString);
                                        Program.ActEstado = true;

                                        break;
                                    case false:
                                        Console.WriteLine("False");
                                        Console.WriteLine(responseString);

                                        Program.ActEstado = false;
                                        break;
                                }
                            }

                        }
                        if (responcevelocidad.IsSuccessStatusCode)
                        {
                            var responseContent = responcevelocidad.Content;
                            string responseString = responseContent.ReadAsStringAsync().Result;
                            var objResponse = JsonConvert.DeserializeObject<RespuestaVelocidad>(responseString).velocidad;
                            if (objResponse != velocidad)
                            {
                                Program.velocidad = objResponse;
                            }

                        }
                        if (responceBloquearRotor.IsSuccessStatusCode)
                        {
                            var responseContent = responceBloquearRotor.Content;
                            string responseString = responseContent.ReadAsStringAsync().Result;
                            var objResponse = JsonConvert.DeserializeObject<RespuestaAccionFuente>(responseString).DescripcionAccion;
                            if (objResponse == "Run")
                            {
                                Program.PararRotor = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error al consultar las APIS" + ex.InnerException);
                    }

                }
            }

        }

        public static void ChangeVelocity()
        {
            byte slaveId = 1;
            port.Open();
            master.WriteSingleRegister(slaveId, 1, Program.velocidad);
            Console.WriteLine("CAMBIO DE VELOCIDAD");

            port.Close();
            port.Dispose();
                    
        }


        public static void RunMotor()
        {
            while (true)
            {
                try
                {
                    if (Program.ActEstado)
                    {
                        byte[] bytesToSend = new byte[8] { 0x01, 0x06, 0x00, 0x00, 0x00, 0x01, 0x48, 0x0A };  //$D0 $F2 $FF $00 $06  01 06 00 00 00 01 48 0A 

                        Console.WriteLine("CORRIENDO MOTOR");
                        port.Open();
                        port.Write(bytesToSend, 0, bytesToSend.Length);
                        port.Close();
                        port.Dispose();
                        ChangeVelocity();
                    }
                    else
                    {
                        byte[] bytesToSend = new byte[8] { 0x01, 0x06, 0x00, 0x00, 0x00, 0x00, 0x89, 0xCA };  //$D0 $F2 $FF $00 $06  01 06 00 00 00 01 48 0A 

                        Console.WriteLine("MOTOR PAUSADO");
                        port.Open();
                        port.Write(bytesToSend, 0, bytesToSend.Length);
                        port.Close();
                        port.Dispose();
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Error al comunicarse con el variador");
                }                
            }
        }

        public static void StopMotor()
        {
            while (true)
            {
                try
                {
                    if (Program.PararRotor)
                    {
                        Console.WriteLine("ROTOR BLOQUEADO");
                        Console.BackgroundColor = ConsoleColor.Red;
                        controller.Write(pin, PinValue.High);
                        Thread.Sleep(10000);
                        controller.Write(pin, PinValue.Low);
                        var finPrueba = ManagementSourceAsync();
                        finPrueba.Wait();
                        Program.PararRotor = false;
                        Console.WriteLine("ROTOR DESBLOQUEADO");

                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Error al bloquear el rotor");
                }               

            }
        }

        public static async Task ManagementSourceAsync()
        {
            HttpClient httpClient = new HttpClient();
            using var requestContent = new MultipartFormDataContent();            

            requestContent.Add(new StringContent("Stop"));
            HttpResponseMessage response = await httpClient.PostAsync("http://damian16-001-site1.htempurl.com/ManagementSource", requestContent);
            Program.PararRotor = false;
        }


        static async Task RunAsync()
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("http://damian16-001-site1.htempurl.com/");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                Console.WriteLine($"Subiendo Imagen...");
                var url = await CreateProductAsync();
                Console.WriteLine($"Respuesta Servidor {url}");
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error en la comunicación con el Circutor");
            }
        }


        static async Task<string> CreateProductAsync()
        {
            var pathGeneral = AppDomain.CurrentDomain.BaseDirectory;
            var imgName = pathGeneral + "file.bmp";

            using (WebClient webClient = new WebClient())
            {
                if (File.Exists(imgName))
                    File.Delete(imgName);
                var routCircutor = "http://169.254.1.49:502/tft.bmp?" + DateTime.Now.Ticks.ToString();
                byte[] data = webClient.DownloadData(routCircutor);
                File.WriteAllBytes(imgName, data);
                Console.WriteLine("Data imagen" + data[0].ToString()); ;

            }

            var fileRout = imgName;
            var fileName = Path.GetFileName(fileRout);

            HttpClient httpClient = new HttpClient();
            using var requestContent = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(fileRout);

            if (Program.PararRotor)
            {
                Console.WriteLine("IMAGEN REGISTROS");
                Console.BackgroundColor = ConsoleColor.Green;

                requestContent.Add(new StreamContent(fileStream), "fileup", fileName);
                HttpResponseMessage response = await httpClient.PostAsync("http://damian16-001-site1.htempurl.com/CargarRegistro", requestContent);
                return response.StatusCode.ToString();
            }
            else
            {
                requestContent.Add(new StreamContent(fileStream), "fileup", fileName);
                HttpResponseMessage response = await httpClient.PostAsync("http://damian16-001-site1.htempurl.com/CargarImagen", requestContent);
                return response.StatusCode.ToString();
            }     
           
        }

        static async Task<string> ReadAccion()
        {
            string respuesta = "";
            Console.WriteLine("Leyendo Accion..");
            HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.GetAsync("http://damian16-001-site1.htempurl.com/Acciones");
            string responseBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode.ToString() == "BadRequest")
            {
                var objError = JsonConvert.DeserializeObject<MessageError>(responseBody);
                if (objError.codigoError == "CCTGAC01")
                {
                    respuesta = objError.mensajeError.ToString();
                    return respuesta;
                }
                else
                {
                    return "Error desconocido";
                }
            }
            else
            {
                var objResponse = JsonConvert.DeserializeObject<RespuestaAccion>(responseBody).CodigoKey;
                var accionNueva = "";
                switch (objResponse)
                {
                    case "0":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                    case "1":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                    case "2":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                    case "3":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                    case "4":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                    case "5":
                        accionNueva = objResponse + "." + DateTime.Now.Ticks.ToString();
                        break;
                }
                HttpResponseMessage accionExe = await httpClient.GetAsync("http://169.254.1.49:502/key.htm?F=" + accionNueva);
                respuesta = "Accion ejecutada";
            }
            return respuesta;
        }

        public static void UpdateAccion()
        {
            while (true)
            {
                try
                {
                    Console.WriteLine($"Actualizando acción...");
                    var accion = ReadAccion().Result;
                    Console.WriteLine($"Respuesta Accion {accion}");
                    System.Threading.Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}